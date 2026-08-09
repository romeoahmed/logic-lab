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
        Ensure(HasValue(document.ProjectId.Value));
        Ensure(GetDisplayTextRule(document.DisplayName) is null);
        Ensure(IsCurrentLibrary(document.LibrarySnapshot));
        Ensure(SymbolProfileCatalog.Contains(document.SymbolProfile));
        Ensure(document.CircuitDefinitions.Count > 0);
        Ensure(HasDistinctIds(
            document.CircuitDefinitions,
            static definition => definition.Id.Value));
        Ensure(HasDistinctIds(
            document.MemoryImages,
            static image => image.Id.Value));
        Ensure(document.FindCircuitDefinition(
            document.EntryCircuitDefinitionId) is not null);

        foreach (var image in document.MemoryImages)
        {
            Ensure(HasValue(image.Id.Value));
            Ensure(ValidateMemoryImage(
                image.DisplayName,
                image.Width,
                image.Depth,
                image.Words).Count == 0);
        }

        foreach (var definition in document.CircuitDefinitions)
        {
            ValidateDefinition(document, definition);
        }

        return new ProjectRevision(revisionId, document);
    }

    private static void ValidateDefinition(
        ProjectDocument document,
        CircuitDefinition definition)
    {
        Ensure(HasValue(definition.Id.Value));
        Ensure(GetDisplayTextRule(definition.DisplayName) is null);
        Ensure(HasDistinctIds(definition.Ports, static port => port.Id.Value));
        Ensure(HasDistinctIds(
            definition.ComponentInstances,
            static instance => instance.Id.Value));
        Ensure(HasDistinctIds(definition.Nets, static net => net.Id.Value));
        Ensure(HasDistinctIds(
            definition.Junctions,
            static junction => junction.Id.Value));
        Ensure(HasDistinctIds(
            definition.WireGeometries,
            static geometry => geometry.Id.Value));
        Ensure(HasDistinctIds(
            definition.Annotations,
            static annotation => annotation.Id.Value));

        foreach (var port in definition.Ports)
        {
            Ensure(HasValue(port.Id.Value));
            Ensure(GetDisplayTextRule(port.DisplayName) is null);
            Ensure(Enum.IsDefined(port.Direction));
            Ensure(port.Width > 0);
            Ensure(Enum.IsDefined(port.Placement.Facing));
        }

        foreach (var instance in definition.ComponentInstances)
        {
            ValidateInstance(document, instance);
        }

        ValidateTopology(document, definition);

        foreach (var annotation in definition.Annotations)
        {
            Ensure(HasValue(annotation.Id.Value));
            Ensure(ValidateAnnotation(new AnnotationValue(
                annotation.Text,
                annotation.Position,
                annotation.Alignment)).Count == 0);
        }
    }

    private static void ValidateInstance(
        ProjectDocument document,
        ComponentInstance instance)
    {
        Ensure(HasValue(instance.Id.Value));
        Ensure(Enum.IsDefined(instance.Placement.QuarterTurnsClockwise));
        if (instance.DisplayName is not null)
        {
            Ensure(GetDisplayTextRule(instance.DisplayName) is null);
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
                    document).Length == 0);
                break;
            case CircuitDefinitionComponentTarget definition:
                Ensure(document.FindCircuitDefinition(
                    definition.CircuitDefinitionId) is not null);
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
        CircuitDefinition definition)
    {
        var nets = definition.Nets.ToDictionary(net => net.Id);
        var junctions = definition.Junctions.ToDictionary(junction => junction.Id);
        var terminalMembership = new HashSet<AuthoredTerminalReference>();
        var junctionMembership = new HashSet<JunctionId>();

        foreach (var net in definition.Nets)
        {
            Ensure(HasValue(net.Id.Value));
            Ensure(net.Width > 0);
            Ensure(net.Terminals.Count > 0
                || net.JunctionIds.Count > 0
                || definition.WireGeometries.Any(geometry => geometry.NetId == net.Id));

            foreach (var terminal in net.Terminals)
            {
                Ensure(terminal.CircuitDefinitionId == definition.Id);
                Ensure(terminalMembership.Add(terminal));
                Ensure(TryGetTerminalWidth(
                    document,
                    definition,
                    terminal,
                    out var width));
                Ensure(width == net.Width);
            }

            foreach (var junctionId in net.JunctionIds)
            {
                Ensure(HasValue(junctionId.Value));
                Ensure(junctionMembership.Add(junctionId));
                Ensure(junctions.TryGetValue(junctionId, out var junction));
                Ensure(junction.NetId == net.Id);
            }
        }

        Ensure(junctionMembership.SetEquals(junctions.Keys));
        foreach (var junction in definition.Junctions)
        {
            Ensure(HasValue(junction.Id.Value));
            Ensure(nets.ContainsKey(junction.NetId));
        }

        foreach (var geometry in definition.WireGeometries)
        {
            Ensure(HasValue(geometry.Id.Value));
            Ensure(nets.ContainsKey(geometry.NetId));
            Ensure(ValidateRoute(geometry.Route) is null);
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
        Func<T, string> selectId)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        return values.All(value => HasValue(selectId(value))
            && ids.Add(selectId(value)));
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
