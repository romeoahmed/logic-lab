using System.Diagnostics.CodeAnalysis;
using LogicLab.Domain.Components;

namespace LogicLab.Domain.Authoring;

public static partial class ProjectEditor
{
    internal static ProjectRevision Rehydrate(
        ProjectRevisionId revisionId,
        ProjectDocument document)
    {
        ArgumentNullException.ThrowIfNull(revisionId);
        ArgumentNullException.ThrowIfNull(document);
        Ensure(HasValue(revisionId.Value));
        ValidateDocument(document);
        return new ProjectRevision(revisionId, document);
    }

    internal static void ValidateDocument(
        ProjectDocument document,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();
        Ensure(HasValue(document.ProjectId.Value));
        Ensure(GetDisplayTextRule(document.DisplayName, cancellationToken) is null);
        Ensure(IsCurrentLibrary(document.LibrarySnapshot));
        Ensure(SymbolProfileRegistry.IsRegistered(document.SymbolProfile));
        Ensure(document.CircuitDefinitions.Count > 0);
        Ensure(HasDistinctIds(
            document.CircuitDefinitions,
            static definition => definition.Id.Value,
            cancellationToken));
        Ensure(HasDistinctIds(
            document.MemoryImages,
            static image => image.Id.Value,
            cancellationToken));
        var index = new DocumentValidationIndex(document, cancellationToken);
        Ensure(index.FindDefinition(document.EntryCircuitDefinitionId) is not null);
        cancellationToken.ThrowIfCancellationRequested();

        foreach (var image in document.MemoryImages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Ensure(HasValue(image.Id.Value));
            Ensure(GetDisplayTextRule(image.DisplayName, cancellationToken) is null);
            Ensure(image.Width > 0);
            Ensure(image.Depth > 0);
        }

        foreach (var definition in document.CircuitDefinitions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateDefinition(
                document,
                definition,
                index,
                cancellationToken);
        }
    }

    private static void ValidateDefinition(
        ProjectDocument document,
        CircuitDefinition definition,
        DocumentValidationIndex index,
        CancellationToken cancellationToken)
    {
        Ensure(HasValue(definition.Id.Value));
        Ensure(GetDisplayTextRule(definition.DisplayName, cancellationToken) is null);
        Ensure(HasDistinctIds(
            definition.Ports,
            static port => port.Id.Value,
            cancellationToken));
        Ensure(HasDistinctIds(
            definition.ComponentInstances,
            static instance => instance.Id.Value,
            cancellationToken));
        Ensure(HasDistinctIds(
            definition.Nets,
            static net => net.Id.Value,
            cancellationToken));
        Ensure(HasDistinctIds(
            definition.Junctions,
            static junction => junction.Id.Value,
            cancellationToken));
        Ensure(HasDistinctIds(
            definition.WireGeometries,
            static geometry => geometry.Id.Value,
            cancellationToken));
        Ensure(HasDistinctIds(
            definition.Annotations,
            static annotation => annotation.Id.Value,
            cancellationToken));

        foreach (var port in definition.Ports)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Ensure(HasValue(port.Id.Value));
            Ensure(GetDisplayTextRule(port.DisplayName, cancellationToken) is null);
            Ensure(Enum.IsDefined(port.Direction));
            Ensure(port.Width > 0);
            Ensure(Enum.IsDefined(port.Placement.Facing));
        }

        foreach (var instance in definition.ComponentInstances)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateInstance(
                document,
                instance,
                index,
                cancellationToken);
        }

        ValidateTopology(
            document,
            definition,
            index,
            cancellationToken);

        foreach (var annotation in definition.Annotations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Ensure(HasValue(annotation.Id.Value));
            Ensure(ValidateAnnotation(
                new AnnotationValue(
                    annotation.Text,
                    annotation.Position,
                    annotation.Alignment),
                cancellationToken).Count == 0);
        }
    }

    private static void ValidateInstance(
        ProjectDocument document,
        ComponentInstance instance,
        DocumentValidationIndex index,
        CancellationToken cancellationToken)
    {
        Ensure(HasValue(instance.Id.Value));
        Ensure(Enum.IsDefined(instance.Placement.QuarterTurnsClockwise));
        if (instance.DisplayName is not null)
        {
            Ensure(GetDisplayTextRule(instance.DisplayName, cancellationToken) is null);
        }

        switch (instance.Target)
        {
            case LibraryComponentTarget library:
                var schema = document.LibrarySnapshot.ResolveContract(
                    library.ContractKey);
                Ensure(schema is not null);
                Ensure(ComponentParameterValidator.ValidateForDocument(
                    library.ContractKey,
                    schema,
                    instance.Parameters,
                    index.MemoryImages,
                    cancellationToken).Length == 0);
                break;
            case CircuitDefinitionComponentTarget definition:
                Ensure(index.FindDefinition(definition.CircuitDefinitionId) is not null);
                cancellationToken.ThrowIfCancellationRequested();
                Ensure(instance.Parameters.Count == 0);
                break;
            default:
                Ensure(condition: false);
                break;
        }

        if (instance.SymbolVariantId is not null)
        {
            Ensure(SymbolVariantCatalog.IsCompatible(
                document.SymbolProfile,
                instance.Target,
                instance.Parameters,
                instance.SymbolVariantId));
        }
    }

    private static void ValidateTopology(
        ProjectDocument document,
        CircuitDefinition definition,
        DocumentValidationIndex index,
        CancellationToken cancellationToken)
    {
        var nets = new Dictionary<NetId, Net>();
        foreach (var net in definition.Nets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            nets.Add(net.Id, net);
        }

        var junctions = new Dictionary<JunctionId, Junction>();
        foreach (var junction in definition.Junctions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            junctions.Add(junction.Id, junction);
        }

        var geometryNetIds = new HashSet<NetId>();
        foreach (var geometry in definition.WireGeometries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            geometryNetIds.Add(geometry.NetId);
        }
        var terminalMembership = new HashSet<AuthoredTerminalReference>();
        var junctionMembership = new HashSet<JunctionId>();

        foreach (var net in definition.Nets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Ensure(HasValue(net.Id.Value));
            Ensure(net.Width > 0);
            Ensure(net.Terminals.Count > 0
                || net.JunctionIds.Count > 0
                || geometryNetIds.Contains(net.Id));

            foreach (var terminal in net.Terminals)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Ensure(terminal.CircuitDefinitionId == definition.Id);
                Ensure(terminalMembership.Add(terminal));
                Ensure(TryGetTerminalWidth(
                    document,
                    definition,
                    terminal,
                    out var width,
                    index));
                Ensure(width == net.Width);
            }

            foreach (var junctionId in net.JunctionIds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Ensure(HasValue(junctionId.Value));
                Ensure(junctionMembership.Add(junctionId));
                Ensure(junctions.TryGetValue(junctionId, out var junction));
                Ensure(junction.NetId == net.Id);
            }
        }

        Ensure(junctionMembership.Count == junctions.Count);
        foreach (var junction in definition.Junctions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Ensure(HasValue(junction.Id.Value));
            Ensure(nets.ContainsKey(junction.NetId));
        }

        foreach (var geometry in definition.WireGeometries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Ensure(HasValue(geometry.Id.Value));
            Ensure(nets.ContainsKey(geometry.NetId));
            Ensure(ValidateRoute(geometry.Route, cancellationToken) is null);
        }
    }

    private static bool IsCurrentLibrary(LibrarySnapshot library)
    {
        return string.Equals(
                library.LibraryId,
                LibrarySnapshot.Core.LibraryId,
                StringComparison.Ordinal)
            && string.Equals(
                library.Version,
                LibrarySnapshot.Core.Version,
                StringComparison.Ordinal)
            && string.Equals(
                library.ContentDigest,
                LibrarySnapshot.Core.ContentDigest,
                StringComparison.Ordinal);
    }

    private static bool HasDistinctIds<T>(
        IEnumerable<T> values,
        Func<T, string> selectId,
        CancellationToken cancellationToken)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = selectId(value);
            if (!HasValue(id) || !ids.Add(id))
            {
                return false;
            }
        }

        return true;
    }

    private static Dictionary<TKey, TValue> IndexBy<TValue, TKey>(
        IEnumerable<TValue> values,
        Func<TValue, TKey> selectKey,
        CancellationToken cancellationToken)
        where TKey : notnull
    {
        var index = new Dictionary<TKey, TValue>();
        foreach (var value in values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            index.Add(selectKey(value), value);
        }

        return index;
    }

    private sealed class DocumentValidationIndex
    {
        private readonly Dictionary<CircuitDefinitionId, CircuitDefinition> definitions;
        private readonly Dictionary<
            CircuitDefinitionId,
            Dictionary<ComponentInstanceId, ComponentInstance>> instances;
        private readonly Dictionary<
            CircuitDefinitionId,
            Dictionary<string, DefinitionPort>> ports;

        public DocumentValidationIndex(
            ProjectDocument document,
            CancellationToken cancellationToken)
        {
            definitions = IndexBy(
                document.CircuitDefinitions,
                static definition => definition.Id,
                cancellationToken);
            MemoryImages = IndexBy(
                document.MemoryImages,
                static image => image.Id,
                cancellationToken);
            instances = new Dictionary<
                CircuitDefinitionId,
                Dictionary<ComponentInstanceId, ComponentInstance>>();
            ports = new Dictionary<
                CircuitDefinitionId,
                Dictionary<string, DefinitionPort>>();
            foreach (var definition in document.CircuitDefinitions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                instances.Add(
                    definition.Id,
                    IndexBy(
                        definition.ComponentInstances,
                        static instance => instance.Id,
                        cancellationToken));
                ports.Add(
                    definition.Id,
                    IndexBy(
                        definition.Ports,
                        static port => port.Id.Value,
                        cancellationToken));
            }
        }

        public Dictionary<MemoryImageId, MemoryImage> MemoryImages { get; }

        public CircuitDefinition? FindDefinition(CircuitDefinitionId id) =>
            definitions.GetValueOrDefault(id);

        public ComponentInstance? FindInstance(
            CircuitDefinitionId definitionId,
            ComponentInstanceId instanceId) =>
            instances[definitionId].GetValueOrDefault(instanceId);

        public DefinitionPort? FindPort(
            CircuitDefinitionId definitionId,
            string portId) =>
            ports[definitionId].GetValueOrDefault(portId);
    }

    private static bool HasValue(string? value) => !string.IsNullOrEmpty(value);

    private static void Ensure([DoesNotReturnIf(false)] bool condition)
    {
        if (!condition)
        {
            throw new ArgumentException(
                "The persisted Project Revision violates an Authoring invariant.");
        }
    }
}
