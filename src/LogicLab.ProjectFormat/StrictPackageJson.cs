using System.Text;
using System.Text.Json;

namespace LogicLab.ProjectFormat;

public static partial class ProjectPackage
{
    private static void ValidateJson(
        ReadOnlySpan<byte> json,
        PackagePolicy policy,
        ulong[] observations)
    {
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
        try
        {
            while (reader.Read())
            {
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
                            var propertyName = reader.GetString()
                                ?? throw Invalid(
                                    "package_json_invalid",
                                    ("rule", "propertyName"));
                            ObserveJsonString(propertyName, policy, observations);
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
                            reader.GetString()
                                ?? throw Invalid(
                                    "package_json_invalid",
                                    ("rule", "string")),
                            policy,
                            observations);
                        break;
                    case JsonTokenType.Number:
                        ValidateIntegerLexeme(reader.ValueSpan);
                        break;
                }
            }
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
        string value,
        PackagePolicy policy,
        ulong[] observations)
    {
        ulong scalarCount = 0;
        foreach (var _ in value.EnumerateRunes())
        {
            scalarCount = SaturatingAdd(scalarCount, 1);
        }

        observations[(int)PackageDimension.StringScalarCount] = SaturatingAdd(
            observations[(int)PackageDimension.StringScalarCount],
            scalarCount);
        observations[(int)PackageDimension.StringUtf8Bytes] = SaturatingAdd(
            observations[(int)PackageDimension.StringUtf8Bytes],
            checked((ulong)Encoding.UTF8.GetByteCount(value)));
        ThrowIfReadLimitExceeded(
            policy,
            observations,
            PackageDimension.StringScalarCount);
        ThrowIfReadLimitExceeded(
            policy,
            observations,
            PackageDimension.StringUtf8Bytes);
    }

    private static void ValidateIntegerLexeme(ReadOnlySpan<byte> value)
    {
        if (value.IndexOfAny((byte)'.', (byte)'e', (byte)'E') >= 0
            || value.SequenceEqual("-0"u8))
        {
            throw Invalid("package_json_invalid", ("rule", "integerLexeme"));
        }
    }

    private static void ValidateManifestMembers(byte[] json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        RequireMembers(
            root,
            "format",
            "schemaVersion",
            "projectPart",
            "memoryParts",
            "packageDigest");
        if (TryGetObject(root, "projectPart", out var projectPart))
        {
            RequireMembers(projectPart, "path", "length", "sha256");
        }

        foreach (var memoryPart in ArrayElements(root, "memoryParts"))
        {
            RequireMembers(
                memoryPart,
                "memoryImageId",
                "path",
                "length",
                "sha256");
        }
    }

    private static void ValidateProjectMembers(byte[] json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        RequireMembers(
            root,
            "projectId",
            "displayName",
            "symbolProfile",
            "libraryReferences",
            "entryCircuitDefinitionId",
            "circuitDefinitions",
            "memoryImages");
        if (TryGetObject(root, "symbolProfile", out var profile))
        {
            RequireMembers(profile, "id", "version", "indicationConvention");
        }

        foreach (var library in ArrayElements(root, "libraryReferences"))
        {
            RequireMembers(library, "id", "version", "digest");
        }

        foreach (var memory in ArrayElements(root, "memoryImages"))
        {
            RequireMembers(
                memory,
                "id",
                "displayName",
                "wordWidth",
                "depth",
                "partPath");
        }

        foreach (var definition in ArrayElements(root, "circuitDefinitions"))
        {
            ValidateDefinitionMembers(definition);
        }
    }

    private static void ValidateDefinitionMembers(JsonElement definition)
    {
        RequireMembers(
            definition,
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
            RequireMembers(port, "id", "displayName", "direction", "width");
        }

        foreach (var instance in ArrayElements(definition, "componentInstances"))
        {
            RequireMembers(instance, "id", "displayName", "target", "parameters");
            if (TryGetObject(instance, "target", out var target))
            {
                ValidateDiscriminatedMembers(
                    target,
                    ("libraryContract", ["kind", "libraryId", "contractId"]),
                    ("circuitDefinition", ["kind", "circuitDefinitionId"]));
            }

            foreach (var parameter in ArrayElements(instance, "parameters"))
            {
                RequireMembers(parameter, "parameterId", "value");
                if (TryGetObject(parameter, "value", out var value))
                {
                    ValidateDiscriminatedMembers(
                        value,
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
                            RequireMembers(slice, "offset", "length");
                        }
                    }
                }
            }
        }

        foreach (var net in ArrayElements(definition, "nets"))
        {
            RequireMembers(net, "id", "width", "terminals", "junctionIds");
            foreach (var terminal in ArrayElements(net, "terminals"))
            {
                ValidateDiscriminatedMembers(
                    terminal,
                    ("definitionPort", ["kind", "portId"]),
                    ("instancePort", ["kind", "componentInstanceId", "portId"]));
            }
        }

        foreach (var junction in ArrayElements(definition, "junctions"))
        {
            RequireMembers(junction, "id", "netId", "position");
            ValidatePoint(junction, "position");
        }

        foreach (var geometry in ArrayElements(definition, "wireGeometry"))
        {
            RequireMembers(geometry, "id", "netId", "route");
            if (TryGetObject(geometry, "route", out var route))
            {
                ValidateDiscriminatedMembers(
                    route,
                    ("unrouted", ["kind"]),
                    ("orthogonal", ["kind", "points"]));
                foreach (var point in ArrayElements(route, "points"))
                {
                    RequireMembers(point, "x", "y");
                }
            }
        }

        if (TryGetObject(definition, "presentation", out var presentation))
        {
            ValidatePresentationMembers(presentation);
        }
    }

    private static void ValidatePresentationMembers(JsonElement presentation)
    {
        RequireMembers(
            presentation,
            "componentPlacements",
            "definitionPortPlacements",
            "annotations");
        foreach (var placement in ArrayElements(presentation, "componentPlacements"))
        {
            RequireMembers(
                placement,
                "componentInstanceId",
                "origin",
                "orientation",
                "symbolVariantId");
            ValidatePoint(placement, "origin");
            if (TryGetObject(placement, "orientation", out var orientation))
            {
                RequireMembers(orientation, "quarterTurnsClockwise", "reflected");
            }
        }

        foreach (var placement in ArrayElements(
                     presentation,
                     "definitionPortPlacements"))
        {
            RequireMembers(placement, "portId", "position", "facing");
            ValidatePoint(placement, "position");
        }

        foreach (var annotation in ArrayElements(presentation, "annotations"))
        {
            RequireMembers(annotation, "id", "text", "position", "alignment");
            ValidatePoint(annotation, "position");
        }
    }

    private static void ValidatePoint(JsonElement owner, string propertyName)
    {
        if (TryGetObject(owner, propertyName, out var point))
        {
            RequireMembers(point, "x", "y");
        }
    }

    private static void ValidateDiscriminatedMembers(
        JsonElement element,
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
                RequireMembers(element, variant.Members);
                return;
            }
        }

        throw Invalid("package_unknown_discriminator");
    }

    private static void RequireMembers(JsonElement element, params string[] members)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var expected = members.ToHashSet(StringComparer.Ordinal);
        if (element.EnumerateObject().Any(property => !expected.Contains(property.Name)))
        {
            throw Invalid("package_unknown_member");
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
