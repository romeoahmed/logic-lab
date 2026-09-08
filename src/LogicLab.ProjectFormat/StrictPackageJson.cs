using System.Buffers;
using System.Buffers.Text;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace LogicLab.ProjectFormat;

public static partial class ProjectPackage
{
    private static void ValidateJson(
        ReadOnlySpan<byte> json,
        PackagePolicy policy,
        ulong[] observations,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var maximumDepth = policy.GetMaximum(PackageDimension.JsonDepth);
        var reader = new Utf8JsonReader(
            json,
            new JsonReaderOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = maximumDepth < int.MaxValue
                    ? checked((int)maximumDepth + 1)
                    : int.MaxValue,
            });
        var containers = new List<JsonContainer>();
        var tokensSinceCancellation = 0;
        try
        {
            while (reader.Read())
            {
                if (++tokensSinceCancellation == CancellationInterval)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    tokensSinceCancellation = 0;
                }

                observations[(int)PackageDimension.JsonTokens] = SaturatingAdd(
                    observations[(int)PackageDimension.JsonTokens],
                    1);
                observations[(int)PackageDimension.JsonDepth] = Math.Max(
                    observations[(int)PackageDimension.JsonDepth],
                    checked((ulong)reader.CurrentDepth + 1));
                ThrowIfReadLimitExceeded(
                    policy,
                    observations,
                    PackageDimension.JsonTokens);
                ThrowIfReadLimitExceeded(
                    policy,
                    observations,
                    PackageDimension.JsonDepth);

                if (IsValueToken(reader.TokenType)
                    && containers.Count > 0
                    && containers[^1].IsArray)
                {
                    observations[(int)PackageDimension.ArrayItems] = SaturatingAdd(
                        observations[(int)PackageDimension.ArrayItems],
                        1);
                    ThrowIfReadLimitExceeded(
                        policy,
                        observations,
                        PackageDimension.ArrayItems);
                    if (reader.TokenType == JsonTokenType.Null)
                    {
                        throw Invalid("package_json_invalid", ("rule", "schema"));
                    }
                }

                switch (reader.TokenType)
                {
                    case JsonTokenType.StartObject:
                        containers.Add(new JsonContainer(isArray: false));
                        break;
                    case JsonTokenType.StartArray:
                        containers.Add(new JsonContainer(isArray: true));
                        break;
                    case JsonTokenType.EndObject:
                    case JsonTokenType.EndArray:
                        containers.RemoveAt(containers.Count - 1);
                        break;
                    case JsonTokenType.PropertyName:
                        {
                            ObserveJsonString(
                                reader.ValueSpan,
                                policy,
                                observations,
                                cancellationToken);
                            var propertyName = reader.GetString()
                                ?? throw Invalid(
                                    "package_json_invalid",
                                    ("rule", "propertyName"));
                            if (containers.Count == 0
                                || containers[^1].IsArray
                                || !containers[^1].PropertyNames.Add(propertyName))
                            {
                                throw Invalid(
                                    "package_json_invalid",
                                    ("rule", "duplicateMember"));
                            }

                            break;
                        }
                    case JsonTokenType.String:
                        ObserveJsonString(
                            reader.ValueSpan,
                            policy,
                            observations,
                            cancellationToken);
                        break;
                    case JsonTokenType.Number:
                        ValidateIntegerLexeme(reader.ValueSpan);
                        break;
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (JsonException)
        {
            throw Invalid("package_json_invalid", ("rule", "syntax"));
        }
    }

    private static bool IsValueToken(JsonTokenType tokenType) => tokenType is
        JsonTokenType.StartObject
        or JsonTokenType.StartArray
        or JsonTokenType.String
        or JsonTokenType.Number
        or JsonTokenType.True
        or JsonTokenType.False
        or JsonTokenType.Null;

    private static void ObserveJsonString(
        ReadOnlySpan<byte> encodedValue,
        PackagePolicy policy,
        ulong[] observations,
        CancellationToken cancellationToken)
    {
        var index = 0;
        var scalarsSinceCancellation = 0;
        while (index < encodedValue.Length)
        {
            if (++scalarsSinceCancellation == CancellationInterval)
            {
                cancellationToken.ThrowIfCancellationRequested();
                scalarsSinceCancellation = 0;
            }

            int utf8Length;
            if (encodedValue[index] != (byte)'\\')
            {
                if (Rune.DecodeFromUtf8(
                        encodedValue[index..],
                        out var rune,
                        out var consumed) != OperationStatus.Done)
                {
                    throw Invalid("package_json_invalid", ("rule", "syntax"));
                }

                utf8Length = rune.Utf8SequenceLength;
                index += consumed;
            }
            else
            {
                (utf8Length, index) = DecodeEscapedScalar(encodedValue, index);
            }

            observations[(int)PackageDimension.StringScalarCount] = SaturatingAdd(
                observations[(int)PackageDimension.StringScalarCount],
                1);
            observations[(int)PackageDimension.StringUtf8Bytes] = SaturatingAdd(
                observations[(int)PackageDimension.StringUtf8Bytes],
                checked((ulong)utf8Length));
            ThrowIfReadLimitExceeded(
                policy,
                observations,
                PackageDimension.StringScalarCount);
            ThrowIfReadLimitExceeded(
                policy,
                observations,
                PackageDimension.StringUtf8Bytes);
        }
    }

    private static (int Utf8Length, int NextIndex) DecodeEscapedScalar(
        ReadOnlySpan<byte> encodedValue,
        int escapeIndex)
    {
        if (escapeIndex + 1 >= encodedValue.Length)
        {
            throw Invalid("package_json_invalid", ("rule", "syntax"));
        }

        var escape = encodedValue[escapeIndex + 1];
        if (escape is (byte)'"' or (byte)'\\' or (byte)'/'
            or (byte)'b' or (byte)'f' or (byte)'n' or (byte)'r' or (byte)'t')
        {
            return (1, escapeIndex + 2);
        }

        if (escape != (byte)'u'
            || !TryReadHex16(encodedValue, escapeIndex + 2, out var codeUnit))
        {
            throw Invalid("package_json_invalid", ("rule", "syntax"));
        }

        var nextIndex = escapeIndex + 6;
        if (char.IsHighSurrogate((char)codeUnit))
        {
            if (nextIndex + 5 >= encodedValue.Length
                || encodedValue[nextIndex] != (byte)'\\'
                || encodedValue[nextIndex + 1] != (byte)'u'
                || !TryReadHex16(encodedValue, nextIndex + 2, out var lowSurrogate)
                || !char.IsLowSurrogate((char)lowSurrogate))
            {
                throw Invalid("package_json_invalid", ("rule", "syntax"));
            }

            _ = Rune.TryCreate(
                (char)codeUnit,
                (char)lowSurrogate,
                out var scalar);
            return (scalar.Utf8SequenceLength, nextIndex + 6);
        }

        if (char.IsLowSurrogate((char)codeUnit))
        {
            throw Invalid("package_json_invalid", ("rule", "syntax"));
        }

        return (new Rune((char)codeUnit).Utf8SequenceLength, nextIndex);
    }

    private static bool TryReadHex16(
        ReadOnlySpan<byte> encodedValue,
        int index,
        out int value)
    {
        value = 0;
        if (index > encodedValue.Length - 4)
        {
            return false;
        }

        return Utf8Parser.TryParse(encodedValue.Slice(index, 4), out value, out var consumed, 'X')
            && consumed == 4;
    }

    private static void ValidateIntegerLexeme(ReadOnlySpan<byte> value)
    {
        if (value.IndexOfAny((byte)'.', (byte)'e', (byte)'E') >= 0
            || value.SequenceEqual("-0"u8))
        {
            throw Invalid("package_json_invalid", ("rule", "integerLexeme"));
        }
    }

    private static void ValidateMembers(
        ReadOnlySpan<byte> json,
        JsonTypeInfo typeInfo,
        CancellationToken cancellationToken)
    {
        var reader = new Utf8JsonReader(json);
        _ = reader.Read();
        ValidateMembers(ref reader, typeInfo, cancellationToken);
    }

    private static void ValidateMembers(
        ref Utf8JsonReader reader,
        JsonTypeInfo typeInfo,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (reader.TokenType == JsonTokenType.StartArray && typeInfo.Type.IsArray)
        {
            var elementType = GetJsonTypeInfo(typeInfo.Type.GetElementType()!);
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                ValidateMembers(ref reader, elementType, cancellationToken);
            }

            return;
        }

        // The strict deserializer owns nullability and scalar types. All V1 members
        // are required, including value-type fields that deserialization can default.
        if (reader.TokenType != JsonTokenType.StartObject || typeInfo.Kind != JsonTypeInfoKind.Object)
        {
            reader.Skip();
            return;
        }

        string? discriminator = null;
        if (typeInfo.PolymorphismOptions is { } polymorphism)
        {
            discriminator = polymorphism.TypeDiscriminatorPropertyName;
            typeInfo = ResolveVariant(reader, polymorphism, cancellationToken);
        }

        var memberCount = 0;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (discriminator is not null && reader.ValueTextEquals(discriminator))
            {
                _ = reader.Read();
                continue;
            }

            JsonPropertyInfo? member = null;
            foreach (var candidate in typeInfo.Properties)
            {
                if (reader.ValueTextEquals(candidate.Name))
                {
                    member = candidate;
                    break;
                }
            }

            if (member is null)
            {
                throw Invalid("package_unknown_member");
            }

            memberCount++;
            _ = reader.Read();
            ValidateMembers(ref reader, GetJsonTypeInfo(member.PropertyType), cancellationToken);
        }

        // The lexical pass already rejected duplicate names.
        if (memberCount != typeInfo.Properties.Count)
        {
            throw Invalid("package_json_invalid", ("rule", "schema"));
        }
    }

    private static JsonTypeInfo ResolveVariant(
        Utf8JsonReader reader,
        JsonPolymorphismOptions polymorphism,
        CancellationToken cancellationToken)
    {
        // Look ahead on a value copy: V1 permits the discriminator after its payload.
        // The lexical pass has already validated syntax and rejected duplicate names.
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var isDiscriminator = reader.ValueTextEquals(polymorphism.TypeDiscriminatorPropertyName);
            _ = reader.Read();
            if (!isDiscriminator)
            {
                reader.Skip();
                continue;
            }

            if (reader.TokenType != JsonTokenType.String)
            {
                break;
            }

            foreach (var variant in polymorphism.DerivedTypes)
            {
                if (variant.TypeDiscriminator is string name && reader.ValueTextEquals(name))
                {
                    return GetJsonTypeInfo(variant.DerivedType);
                }
            }

            throw Invalid("package_unknown_discriminator");
        }

        throw Invalid("package_json_invalid", ("rule", "schema"));
    }

    private static JsonTypeInfo GetJsonTypeInfo(Type type) =>
        ReadJsonContext.GetTypeInfo(type)
        ?? throw new InvalidOperationException("The package DTO has no generated JSON metadata.");

    private sealed class JsonContainer(bool isArray)
    {
        public bool IsArray { get; } = isArray;

        public HashSet<string> PropertyNames { get; } = new(StringComparer.Ordinal);
    }
}
