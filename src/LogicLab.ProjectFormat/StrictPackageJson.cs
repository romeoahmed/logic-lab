using System.Buffers;
using System.Text;
using System.Text.Json;

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
        var maximumDepth = policy.Maximum(PackageDimension.JsonDepth);
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

        for (var offset = 0; offset < 4; offset++)
        {
            var digit = encodedValue[index + offset] switch
            {
                >= (byte)'0' and <= (byte)'9' =>
                    encodedValue[index + offset] - (byte)'0',
                >= (byte)'a' and <= (byte)'f' =>
                    encodedValue[index + offset] - (byte)'a' + 10,
                >= (byte)'A' and <= (byte)'F' =>
                    encodedValue[index + offset] - (byte)'A' + 10,
                _ => -1,
            };
            if (digit < 0)
            {
                return false;
            }

            value = (value << 4) | digit;
        }

        return true;
    }

    private static void ValidateIntegerLexeme(ReadOnlySpan<byte> value)
    {
        if (value.IndexOfAny((byte)'.', (byte)'e', (byte)'E') >= 0
            || value.SequenceEqual("-0"u8))
        {
            throw Invalid("package_json_invalid", ("rule", "integerLexeme"));
        }
    }

    private static async Task ValidateManifestMembersAsync(
        byte[] json,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var stream = new MemoryStream(json, writable: false);
        using var document = await JsonDocument.ParseAsync(
            stream,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var root = document.RootElement;
        RequireMembers(
            root,
            cancellationToken,
            "format",
            "schemaVersion",
            "projectPart",
            "memoryParts",
            "packageDigest");
        if (TryGetObject(root, "projectPart", out var projectPart))
        {
            RequireMembers(
                projectPart,
                cancellationToken,
                "path",
                "length",
                "sha256");
        }

        foreach (var memoryPart in ArrayElements(root, "memoryParts"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequireMembers(
                memoryPart,
                cancellationToken,
                "memoryImageId",
                "path",
                "length",
                "sha256");
        }
    }

    private static async Task ValidateProjectMembersAsync(
        byte[] json,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var stream = new MemoryStream(json, writable: false);
        using var document = await JsonDocument.ParseAsync(
            stream,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var root = document.RootElement;
        RequireMembers(
            root,
            cancellationToken,
            "projectId",
            "displayName",
            "symbolProfile",
            "libraryReferences",
            "entryCircuitDefinitionId",
            "circuitDefinitions",
            "memoryImages");
        if (TryGetObject(root, "symbolProfile", out var profile))
        {
            RequireMembers(
                profile,
                cancellationToken,
                "id",
                "version",
                "indicationConvention");
        }

        foreach (var library in ArrayElements(root, "libraryReferences"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequireMembers(
                library,
                cancellationToken,
                "id",
                "version",
                "digest");
        }

        foreach (var memory in ArrayElements(root, "memoryImages"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequireMembers(
                memory,
                cancellationToken,
                "id",
                "displayName",
                "wordWidth",
                "depth",
                "partPath");
        }

        foreach (var definition in ArrayElements(root, "circuitDefinitions"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateDefinitionMembers(definition, cancellationToken);
        }
    }

    private static void ValidateDefinitionMembers(
        JsonElement definition,
        CancellationToken cancellationToken)
    {
        RequireMembers(
            definition,
            cancellationToken,
            "id",
            "displayName",
            "ports",
            "componentInstances",
            "nets",
            "junctions",
            "wireGeometry",
            "presentation");
        foreach (var port in ArrayElements(definition, "ports"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequireMembers(
                port,
                cancellationToken,
                "id",
                "displayName",
                "direction",
                "width");
        }

        foreach (var instance in ArrayElements(definition, "componentInstances"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequireMembers(
                instance,
                cancellationToken,
                "id",
                "displayName",
                "target",
                "parameters");
            if (TryGetObject(instance, "target", out var target))
            {
                ValidateDiscriminatedMembers(
                    target,
                    cancellationToken,
                    ("libraryContract", ["kind", "libraryId", "contractId"]),
                    ("circuitDefinition", ["kind", "circuitDefinitionId"]));
            }

            foreach (var parameter in ArrayElements(instance, "parameters"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                RequireMembers(
                    parameter,
                    cancellationToken,
                    "parameterId",
                    "value");
                if (TryGetObject(parameter, "value", out var value))
                {
                    ValidateDiscriminatedMembers(
                        value,
                        cancellationToken,
                        ("unsigned32", ["kind", "value"]),
                        ("unsigned64", ["kind", "decimal"]),
                        ("enum", ["kind", "value"]),
                        ("logicVector", ["kind", "bits"]),
                        ("unsigned32List", ["kind", "values"]),
                        ("sliceList", ["kind", "values"]),
                        ("memoryImage", ["kind", "memoryImageId"]));
                    if (TryGetString(value, "kind") == "sliceList")
                    {
                        foreach (var slice in ArrayElements(value, "values"))
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            RequireMembers(
                                slice,
                                cancellationToken,
                                "offset",
                                "length");
                        }
                    }
                }
            }
        }

        foreach (var net in ArrayElements(definition, "nets"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequireMembers(
                net,
                cancellationToken,
                "id",
                "width",
                "terminals",
                "junctionIds");
            foreach (var terminal in ArrayElements(net, "terminals"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                ValidateDiscriminatedMembers(
                    terminal,
                    cancellationToken,
                    ("definitionPort", ["kind", "portId"]),
                    ("instancePort", ["kind", "componentInstanceId", "portId"]));
            }
        }

        foreach (var junction in ArrayElements(definition, "junctions"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequireMembers(
                junction,
                cancellationToken,
                "id",
                "netId",
                "position");
            ValidatePoint(junction, "position", cancellationToken);
        }

        foreach (var geometry in ArrayElements(definition, "wireGeometry"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequireMembers(
                geometry,
                cancellationToken,
                "id",
                "netId",
                "route");
            if (TryGetObject(geometry, "route", out var route))
            {
                ValidateDiscriminatedMembers(
                    route,
                    cancellationToken,
                    ("unrouted", ["kind"]),
                    ("orthogonal", ["kind", "points"]));
                foreach (var point in ArrayElements(route, "points"))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    RequireMembers(point, cancellationToken, "x", "y");
                }
            }
        }

        if (TryGetObject(definition, "presentation", out var presentation))
        {
            ValidatePresentationMembers(presentation, cancellationToken);
        }
    }

    private static void ValidatePresentationMembers(
        JsonElement presentation,
        CancellationToken cancellationToken)
    {
        RequireMembers(
            presentation,
            cancellationToken,
            "componentPlacements",
            "definitionPortPlacements",
            "annotations");
        foreach (var placement in ArrayElements(presentation, "componentPlacements"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequireMembers(
                placement,
                cancellationToken,
                "componentInstanceId",
                "origin",
                "orientation",
                "symbolVariantId");
            ValidatePoint(placement, "origin", cancellationToken);
            if (TryGetObject(placement, "orientation", out var orientation))
            {
                RequireMembers(
                    orientation,
                    cancellationToken,
                    "quarterTurnsClockwise",
                    "reflected");
            }
        }

        foreach (var placement in ArrayElements(
                     presentation,
                     "definitionPortPlacements"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequireMembers(
                placement,
                cancellationToken,
                "portId",
                "position",
                "facing");
            ValidatePoint(placement, "position", cancellationToken);
        }

        foreach (var annotation in ArrayElements(presentation, "annotations"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequireMembers(
                annotation,
                cancellationToken,
                "id",
                "text",
                "position",
                "alignment");
            ValidatePoint(annotation, "position", cancellationToken);
        }
    }

    private static void ValidatePoint(
        JsonElement owner,
        string propertyName,
        CancellationToken cancellationToken)
    {
        if (TryGetObject(owner, propertyName, out var point))
        {
            RequireMembers(point, cancellationToken, "x", "y");
        }
    }

    private static void ValidateDiscriminatedMembers(
        JsonElement element,
        CancellationToken cancellationToken,
        params (string Kind, string[] Members)[] variants)
    {
        var kind = TryGetString(element, "kind");
        if (kind is null)
        {
            return;
        }

        foreach (var variant in variants)
        {
            if (string.Equals(kind, variant.Kind, StringComparison.Ordinal))
            {
                RequireMembers(element, cancellationToken, variant.Members);
                return;
            }
        }

        throw Invalid("package_unknown_discriminator");
    }

    private static void RequireMembers(
        JsonElement element,
        CancellationToken cancellationToken,
        params string[] members)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var expected = members.ToHashSet(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!expected.Contains(property.Name))
            {
                throw Invalid("package_unknown_member");
            }
        }
    }

    private static bool TryGetObject(
        JsonElement owner,
        string propertyName,
        out JsonElement value)
    {
        if (owner.ValueKind == JsonValueKind.Object
            && owner.TryGetProperty(propertyName, out value)
            && value.ValueKind == JsonValueKind.Object)
        {
            return true;
        }

        value = default;
        return false;
    }

    private static JsonElement.ArrayEnumerator ArrayElements(
        JsonElement owner,
        string propertyName)
    {
        return owner.ValueKind == JsonValueKind.Object
            && owner.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.Array
                ? value.EnumerateArray()
                : default;
    }

    private static string? TryGetString(JsonElement owner, string propertyName)
    {
        return owner.ValueKind == JsonValueKind.Object
            && owner.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }

    private sealed class JsonContainer(bool isArray)
    {
        public bool IsArray { get; } = isArray;

        public HashSet<string> PropertyNames { get; } = new(StringComparer.Ordinal);
    }
}
