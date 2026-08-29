using System.Globalization;
using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.Engine.Compilation;

namespace LogicLab.Web.Scene;

internal sealed class SceneIntentTranslator
{
    private readonly ProjectDocument document;
    private readonly CircuitDefinition definition;

    public SceneIntentTranslator(ProjectDocument document, CircuitDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(definition);
        this.document = document;
        this.definition = definition;
    }

    public EditIntent TranslateEdit(SceneIntentV1 intent)
    {
        ArgumentNullException.ThrowIfNull(intent);
        ValidateSnapModifier(intent switch
        {
            PlaceComponentSceneIntentV1 place => place.SnapModifier,
            MoveComponentsSceneIntentV1 move => move.SnapModifier,
            MoveDefinitionPortsSceneIntentV1 move => move.SnapModifier,
            MoveAnnotationsSceneIntentV1 move => move.SnapModifier,
            CommitWireSceneIntentV1 wire => wire.SnapModifier,
            AddJunctionSceneIntentV1 add => add.SnapModifier,
            RemoveJunctionSceneIntentV1 remove => remove.SnapModifier,
            SetWireRouteSceneIntentV1 route => route.SnapModifier,
            SelectSourcesSceneIntentV1 => throw new InvalidOperationException(
                "Selection is Web state, not a Project edit."),
            ToggleProbeSceneIntentV1 => throw new InvalidOperationException(
                "Probe state is a Workspace command, not a Project edit."),
            _ => throw new InvalidOperationException("The Scene Intent variant is undefined."),
        });

        return intent switch
        {
            PlaceComponentSceneIntentV1 place => TranslatePlace(place),
            MoveComponentsSceneIntentV1 move => new MoveComponentInstancesIntent(
                definition.Id,
                [.. move.Moves.Select(item => new ComponentMove(
                    ResolveComponent(item.Component).Id,
                    TranslatePlacement(item.Placement)))]),
            MoveDefinitionPortsSceneIntentV1 move => new MoveDefinitionPortsIntent(
                definition.Id,
                [.. move.Moves.Select(item => new DefinitionPortMove(
                    ResolveDefinitionPort(item.Port).Id,
                    TranslatePlacement(item.Placement)))]),
            MoveAnnotationsSceneIntentV1 move => new MoveAnnotationsIntent(
                definition.Id,
                [.. move.Moves.Select(item => new AnnotationMove(
                    ResolveAnnotation(item.Annotation).Id,
                    TranslatePoint(item.Position)))]),
            CommitWireSceneIntentV1 wire => new ConnectTerminalsIntent(
                [.. wire.Terminals.Select(TranslateTerminal)],
                wire.DestinationNet is null
                    ? null
                    : ResolveNet(wire.DestinationNet).Id,
                [.. wire.NewJunctionPositions.Select(TranslatePoint)],
                [.. wire.RouteAdditions.Select(TranslateRoute)],
                [.. wire.RouteReplacements.Select(TranslateReplacement)]),
            AddJunctionSceneIntentV1 add => new AddJunctionIntent(
                definition.Id,
                ResolveNet(add.Net).Id,
                TranslatePoint(add.Position),
                [.. add.RouteAdditions.Select(TranslateRoute)],
                [.. add.RouteReplacements.Select(TranslateReplacement)],
                [.. add.RouteRemovals.Select(item => ResolveWireGeometry(item).Id)]),
            RemoveJunctionSceneIntentV1 remove => new RemoveJunctionIntent(
                definition.Id,
                ResolveJunction(remove.Junction).Id,
                [.. remove.ResultingPartitions.Select(TranslatePartition)],
                [.. remove.RouteReplacements.Select(TranslateReplacement)],
                [.. remove.RouteRemovals.Select(item => ResolveWireGeometry(item).Id)]),
            SetWireRouteSceneIntentV1 route => new SetWireGeometryIntent(
                definition.Id,
                ResolveWireGeometry(route.WireGeometry).Id,
                TranslateRoute(route.Route)),
            _ => throw new InvalidOperationException("The Scene Intent variant is undefined."),
        };
    }

    public EditIntent? TranslateRemoval(SceneSourceRefV1 source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return source.EntityKind switch
        {
            "componentInstance" => definition.ComponentInstances.SingleOrDefault(item =>
                IsSource(source, "componentInstance", item.Id.Value)) is { } component
                    ? new RemoveComponentInstancesIntent(definition.Id, [component.Id])
                    : null,
            "wireGeometry" => definition.WireGeometries.SingleOrDefault(item =>
                IsSource(source, "wireGeometry", item.Id.Value)) is { } wire
                    ? new RemoveWireGeometryIntent(definition.Id, wire.Id)
                    : null,
            "annotation" => definition.Annotations.SingleOrDefault(item =>
                IsSource(source, "annotation", item.Id.Value)) is { } annotation
                    ? new RemoveAnnotationIntent(definition.Id, annotation.Id)
                    : null,
            _ => null,
        };
    }

    public CompilationSource TranslateProbe(SceneElaboratedNetRefV1 reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        var net = ResolveNet(reference.AuthoredNet);
        var path = reference.HierarchyPath;
        if (!string.Equals(
                path.EntryCircuitDefinitionId,
                document.EntryCircuitDefinitionId.Value,
                StringComparison.Ordinal))
        {
            throw new ArgumentException("The Probe hierarchy entry is invalid.");
        }

        var current = document.EntryCircuitDefinition;
        var steps = new HierarchyPathStep[path.Steps.Count];
        for (var index = 0; index < path.Steps.Count; index++)
        {
            var step = path.Steps[index];
            if (!string.Equals(
                    step.ContainingCircuitDefinitionId,
                    current.Id.Value,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException("The Probe hierarchy path is discontinuous.");
            }

            var instance = current.ComponentInstances.Single(candidate => string.Equals(
                candidate.Id.Value,
                step.ComponentInstanceId,
                StringComparison.Ordinal));
            if (instance.Target is not CircuitDefinitionComponentTarget target
                || document.FindCircuitDefinition(target.CircuitDefinitionId) is not { } child)
            {
                throw new ArgumentException("The Probe hierarchy step is not elaboratable.");
            }

            steps[index] = new HierarchyPathStep(current.Id, instance.Id);
            current = child;
        }

        if (current.Id != definition.Id)
        {
            throw new ArgumentException(
                "The Probe hierarchy does not reach the Scene definition.");
        }

        return new CompilationSource(
            new NetSourceIdentity(definition.Id, net.Id),
            new HierarchyPath(document.EntryCircuitDefinitionId, steps));
    }

    private EditIntent TranslatePlace(PlaceComponentSceneIntentV1 intent)
    {
        var target = TranslateTarget(intent.Target);
        var placement = TranslatePlacement(intent.Placement);
        var newMemoryImages = intent.Parameters
            .Where(binding => binding.Value is SceneNewMemoryImageParameterV1)
            .ToArray();
        if (newMemoryImages.Length == 0)
        {
            return new PlaceComponentInstanceIntent(
                definition.Id,
                target,
                [.. intent.Parameters.Select(TranslateParameter)],
                placement,
                intent.DisplayName);
        }

        if (target is not LibraryComponentTarget library || newMemoryImages.Length != 1)
        {
            throw new InvalidOperationException(
                "A component placement may create exactly one library Memory Image.");
        }

        var memoryBinding = newMemoryImages[0];
        var memory = (SceneNewMemoryImageParameterV1)memoryBinding.Value;
        return new PlaceComponentWithNewMemoryImageIntent(
            definition.Id,
            library.ContractKey,
            [.. intent.Parameters
                .Where(binding => binding.Value is not SceneNewMemoryImageParameterV1)
                .Select(TranslateParameter)],
            new NewMemoryImageBinding(
                memoryBinding.ParameterId,
                memory.DisplayName,
                memory.Width,
                memory.Depth,
                [.. memory.Words.Select(word =>
                    new MemoryImageWord(ParseLogicVector(word)))]),
            placement,
            intent.DisplayName);
    }

    private ComponentTarget TranslateTarget(SceneComponentTargetV1 target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return target switch
        {
            SceneLibraryComponentTargetV1 library => new LibraryComponentTarget(
                new ComponentContractKey(library.LibraryId, library.ContractId)),
            SceneCircuitDefinitionTargetV1 circuit => new CircuitDefinitionComponentTarget(
                document.CircuitDefinitions.Single(candidate => string.Equals(
                    candidate.Id.Value,
                    circuit.CircuitDefinitionId,
                    StringComparison.Ordinal)).Id),
            _ => throw new InvalidOperationException("The Scene component target is undefined."),
        };
    }

    private ComponentParameterBinding TranslateParameter(SceneParameterBindingV1 binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentException.ThrowIfNullOrEmpty(binding.ParameterId);
        ComponentParameterValue value = binding.Value switch
        {
            SceneUnsigned32ParameterV1 unsigned32 =>
                new Unsigned32ParameterValue(unsigned32.Value),
            SceneUnsigned64ParameterV1 unsigned64 =>
                new Unsigned64ParameterValue(ParseCanonicalUnsigned64(unsigned64.DecimalText)),
            SceneChoiceParameterV1 choice => new ChoiceParameterValue(choice.Value),
            SceneLogicVectorParameterV1 vector =>
                new LogicVectorParameterValue(ParseLogicVector(vector.Bits)),
            SceneWidthsParameterV1 widths => new WidthsParameterValue(widths.Values),
            SceneSlicesParameterV1 slices => new SlicesParameterValue(
                [.. slices.Values.Select(slice => new BitSlice(slice.Offset, slice.Length))]),
            SceneMemoryImageParameterV1 memory => new MemoryImageParameterValue(
                document.MemoryImages.Single(image => string.Equals(
                    image.Id.Value,
                    memory.MemoryImageId,
                    StringComparison.Ordinal)).Id),
            _ => throw new InvalidOperationException("The Scene parameter value is undefined."),
        };
        return new ComponentParameterBinding(binding.ParameterId, value);
    }

    private static ComponentPlacement TranslatePlacement(
        SceneComponentPlacementV1 placement)
    {
        ArgumentNullException.ThrowIfNull(placement);
        if (placement.QuarterTurnsClockwise is < 0 or > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(placement));
        }

        return new ComponentPlacement(
            TranslatePoint(placement.Origin),
            (QuarterTurn)placement.QuarterTurnsClockwise,
            placement.Reflected);
    }

    private static DefinitionPortPlacement TranslatePlacement(
        SceneDefinitionPortPlacementV1 placement)
    {
        ArgumentNullException.ThrowIfNull(placement);
        var facing = placement.Facing switch
        {
            "north" => CardinalDirection.North,
            "east" => CardinalDirection.East,
            "south" => CardinalDirection.South,
            "west" => CardinalDirection.West,
            _ => throw new ArgumentOutOfRangeException(nameof(placement)),
        };
        return new DefinitionPortPlacement(TranslatePoint(placement.Position), facing);
    }

    private static GridPoint TranslatePoint(SceneGridPointV1 point)
    {
        ArgumentNullException.ThrowIfNull(point);
        return new GridPoint(point.X, point.Y);
    }

    private static WireRoute TranslateRoute(SceneWireRouteV1 route)
    {
        ArgumentNullException.ThrowIfNull(route);
        return route switch
        {
            SceneUnroutedWireRouteV1 => new UnroutedWireRoute(),
            SceneOrthogonalWireRouteV1 orthogonal => new OrthogonalWireRoute(
                [.. orthogonal.Points.Select(TranslatePoint)]),
            _ => throw new InvalidOperationException("The Scene wire route is undefined."),
        };
    }

    private static void ValidateSnapModifier(string value)
    {
        if (value is not ("none" or "disableSnap"))
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }
    }

    private static ulong ParseCanonicalUnsigned64(string value)
    {
        if (string.IsNullOrEmpty(value)
            || (value.Length > 1 && value[0] == '0')
            || !value.All(char.IsAsciiDigit)
            || !ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
        {
            throw new FormatException("The unsigned 64-bit value is not canonical.");
        }

        return parsed;
    }

    private static LogicValue[] ParseLogicVector(string bits)
    {
        ArgumentException.ThrowIfNullOrEmpty(bits);
        var values = new LogicValue[bits.Length];
        for (var index = 0; index < bits.Length; index++)
        {
            values[bits.Length - index - 1] = bits[index] switch
            {
                '0' => LogicValue.Zero,
                '1' => LogicValue.One,
                'X' => LogicValue.X,
                _ => throw new FormatException("The Logic Vector text is invalid."),
            };
        }

        return values;
    }

    private AuthoredTerminalReference TranslateTerminal(SceneTerminalRefV1 terminal)
    {
        ArgumentNullException.ThrowIfNull(terminal);
        if (!string.Equals(
                terminal.CircuitDefinitionId,
                definition.Id.Value,
                StringComparison.Ordinal))
        {
            throw new ArgumentException("The terminal is outside the Scene definition.");
        }

        return terminal switch
        {
            SceneDefinitionTerminalRefV1 port => new DefinitionTerminalReference(
                definition.Id,
                definition.Ports.Single(candidate => string.Equals(
                    candidate.Id.Value,
                    port.PortId,
                    StringComparison.Ordinal)).Id),
            SceneInstanceTerminalRefV1 instance => new InstanceTerminalReference(
                definition.Id,
                definition.ComponentInstances.Single(candidate => string.Equals(
                    candidate.Id.Value,
                    instance.ComponentInstanceId,
                    StringComparison.Ordinal)).Id,
                instance.PortId),
            _ => throw new InvalidOperationException("The Scene terminal is undefined."),
        };
    }

    private WireGeometryReplacement TranslateReplacement(SceneWireReplacementV1 replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        return new WireGeometryReplacement(
            ResolveWireGeometry(replacement.WireGeometry).Id,
            TranslateRoute(replacement.Route));
    }

    private JunctionRemovalPartition TranslatePartition(
        SceneJunctionRemovalPartitionV1 partition)
    {
        ArgumentNullException.ThrowIfNull(partition);
        return new JunctionRemovalPartition(
            new NetPartition(
                [.. partition.Membership.Terminals.Select(TranslateTerminal)],
                [.. partition.Membership.Junctions.Select(item => ResolveJunction(item).Id)],
                [.. partition.Membership.WireGeometries.Select(item =>
                    ResolveWireGeometry(item).Id)]),
            [.. partition.RouteAdditions.Select(TranslateRoute)]);
    }

    private ComponentInstance ResolveComponent(SceneSourceRefV1 source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return definition.ComponentInstances.Single(candidate =>
            IsSource(source, "componentInstance", candidate.Id.Value));
    }

    private DefinitionPort ResolveDefinitionPort(SceneSourceRefV1 source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return definition.Ports.Single(candidate =>
            IsSource(source, "definitionPort", candidate.Id.Value));
    }

    private Annotation ResolveAnnotation(SceneSourceRefV1 source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return definition.Annotations.Single(candidate =>
            IsSource(source, "annotation", candidate.Id.Value));
    }

    private Net ResolveNet(SceneSourceRefV1 source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return definition.Nets.Single(candidate =>
            IsSource(source, "net", candidate.Id.Value));
    }

    private Junction ResolveJunction(SceneSourceRefV1 source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return definition.Junctions.Single(candidate =>
            IsSource(source, "junction", candidate.Id.Value));
    }

    private WireGeometry ResolveWireGeometry(SceneSourceRefV1 source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return definition.WireGeometries.Single(candidate =>
            IsSource(source, "wireGeometry", candidate.Id.Value));
    }

    private bool IsSource(SceneSourceRefV1 source, string kind, string id) => string.Equals(
        source.CircuitDefinitionId,
        definition.Id.Value,
        StringComparison.Ordinal)
        && string.Equals(source.EntityKind, kind, StringComparison.Ordinal)
        && string.Equals(source.EntityId, id, StringComparison.Ordinal)
        && source.PortId is null;
}
