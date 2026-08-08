using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using LogicLab.Domain.Authoring;

namespace LogicLab.Infrastructure.Persistence;

internal static class ProjectRevisionPayloadSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        TypeInfoResolver = new DomainPolymorphicTypeResolver(),
    };

    public static byte[] Serialize(ProjectRevision revision)
    {
        ArgumentNullException.ThrowIfNull(revision);
        return JsonSerializer.SerializeToUtf8Bytes(revision, Options);
    }

    private sealed class DomainPolymorphicTypeResolver : DefaultJsonTypeInfoResolver
    {
        private static readonly Type[] ConcreteDomainTypes =
        [
            .. typeof(ProjectRevision).Assembly
                .GetTypes()
                .Where(type => !type.IsAbstract && !type.IsInterface)
                .OrderBy(type => type.FullName, StringComparer.Ordinal),
        ];

        public override JsonTypeInfo GetTypeInfo(
            Type type,
            JsonSerializerOptions options)
        {
            var typeInfo = base.GetTypeInfo(type, options);
            if (!type.IsAbstract || type.Assembly != typeof(ProjectRevision).Assembly)
            {
                return typeInfo;
            }

            var derivedTypes = ConcreteDomainTypes
                .Where(candidate => candidate.IsAssignableTo(type))
                .ToArray();
            if (derivedTypes.Length == 0)
            {
                return typeInfo;
            }

            typeInfo.PolymorphismOptions = new JsonPolymorphismOptions
            {
                TypeDiscriminatorPropertyName = "$type",
                UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization,
            };
            foreach (var derivedType in derivedTypes)
            {
                typeInfo.PolymorphismOptions.DerivedTypes.Add(
                    new JsonDerivedType(derivedType, derivedType.FullName!));
            }

            return typeInfo;
        }
    }
}
