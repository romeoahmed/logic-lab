using System.Globalization;
using LogicLab.Application.Workspaces;
using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.Engine.Compilation;
using LogicLab.Web.Scene;

namespace LogicLab.Web.Components.Pages;

public sealed partial class Editor
{
    private async Task HandleSceneSemanticActionAsync(SceneSemanticActionV1 action)
    {
        if (action is not RemoveSceneSemanticActionV1 remove
            || Projection is null
            || SelectedDefinitionId is null
            || !string.Equals(
                remove.Source.CircuitDefinitionId,
                SelectedDefinitionId.Value,
                StringComparison.Ordinal))
        {
            return;
        }

        var definition = Projection.ProjectRevision.Document
            .FindCircuitDefinition(SelectedDefinitionId);
        if (definition is null)
        {
            return;
        }

        var intent = TranslateSemanticRemoval(definition, remove.Source);
        if (intent is not null)
        {
            _ = await Apply(intent);
        }
    }

    private static EditIntent? TranslateSemanticRemoval(
        CircuitDefinition definition,
        SceneSourceRefV1 source) => source.EntityKind switch
        {
            "componentInstance" => definition.ComponentInstances.SingleOrDefault(item =>
                IsSource(source, definition, "componentInstance", item.Id.Value)) is { } component
                    ? new RemoveComponentInstancesIntent(definition.Id, [component.Id])
                    : null,
            "wireGeometry" => definition.WireGeometries.SingleOrDefault(item =>
                IsSource(source, definition, "wireGeometry", item.Id.Value)) is { } wire
                    ? new RemoveWireGeometryIntent(definition.Id, wire.Id)
                    : null,
            "annotation" => definition.Annotations.SingleOrDefault(item =>
                IsSource(source, definition, "annotation", item.Id.Value)) is { } annotation
                    ? new RemoveAnnotationIntent(definition.Id, annotation.Id)
                    : null,
            _ => null,
        };

    private async Task HandleSceneIntentAsync(SceneIntentV1 intent)
    {
        ArgumentNullException.ThrowIfNull(intent);
        try
        {
            var definition = ResolveIntentDefinition(intent);
            if (intent is ToggleProbeSceneIntentV1 toggleProbe)
            {
                await ToggleProbeAsync(toggleProbe, definition);
                return;
            }

            var edit = TranslateSceneEditIntent(intent, definition);
            if (edit is not null)
            {
                _ = await Apply(edit);
            }
        }
        catch (Exception exception) when (exception is ArgumentException
            or FormatException
            or InvalidOperationException
            or OverflowException)
        {
            // Browser input is untrusted. CircuitSceneHost already invalidated its
            // publication key, so an invalid known intent receives a full snapshot.
            return;
        }
    }

    private EditIntent? TranslateSceneEditIntent(
        SceneIntentV1 intent,
        CircuitDefinition definition)
    {
        return intent switch
        {
            PlaceComponentSceneIntentV1 place => new PlaceComponentInstanceIntent(
                definition.Id,
                TranslateTarget(place.Target),
                [.. place.Parameters.Select(TranslateParameter)],
                TranslatePlacement(place.Placement, place.SnapModifier),
                place.DisplayName),
            MoveComponentsSceneIntentV1 move => new MoveComponentInstancesIntent(
                definition.Id,
                [.. move.Moves.Select(item => new ComponentMove(
                    ResolveComponent(definition, item.Component).Id,
                    TranslatePlacement(item.Placement, move.SnapModifier)))]),
            MoveDefinitionPortsSceneIntentV1 move => new MoveDefinitionPortsIntent(
                definition.Id,
                [.. move.Moves.Select(item => new DefinitionPortMove(
                    ResolveDefinitionPort(definition, item.Port).Id,
                    TranslatePlacement(item.Placement, move.SnapModifier)))]),
            MoveAnnotationsSceneIntentV1 move => new MoveAnnotationsIntent(
                definition.Id,
                [.. move.Moves.Select(item => new AnnotationMove(
                    ResolveAnnotation(definition, item.Annotation).Id,
                    TranslatePoint(item.Position, move.SnapModifier)))]),
            CommitWireSceneIntentV1 wire => new ConnectTerminalsIntent(
                [.. wire.Terminals.Select(item => TranslateTerminal(definition, item))],
                wire.DestinationNet is null
                    ? null
                    : ResolveNet(definition, wire.DestinationNet).Id,
                [.. wire.NewJunctionPositions.Select(item =>
                    TranslatePoint(item, wire.SnapModifier))],
                [.. wire.RouteAdditions.Select(item =>
                    TranslateRoute(item, wire.SnapModifier))],
                [.. wire.RouteReplacements.Select(item =>
                    TranslateReplacement(definition, item, wire.SnapModifier))]),
            AddJunctionSceneIntentV1 add => new AddJunctionIntent(
                definition.Id,
                ResolveNet(definition, add.Net).Id,
                TranslatePoint(add.Position, add.SnapModifier),
                [.. add.RouteAdditions.Select(item =>
                    TranslateRoute(item, add.SnapModifier))],
                [.. add.RouteReplacements.Select(item =>
                    TranslateReplacement(definition, item, add.SnapModifier))],
                [.. add.RouteRemovals.Select(item =>
                    ResolveWireGeometry(definition, item).Id)]),
            RemoveJunctionSceneIntentV1 remove => new RemoveJunctionIntent(
                definition.Id,
                ResolveJunction(definition, remove.Junction).Id,
                [.. remove.ResultingPartitions.Select(item =>
                    TranslatePartition(definition, item, remove.SnapModifier))],
                [.. remove.RouteReplacements.Select(item =>
                    TranslateReplacement(definition, item, remove.SnapModifier))],
                [.. remove.RouteRemovals.Select(item =>
                    ResolveWireGeometry(definition, item).Id)]),
            SetWireRouteSceneIntentV1 route => new SetWireGeometryIntent(
                definition.Id,
                ResolveWireGeometry(definition, route.WireGeometry).Id,
                TranslateRoute(route.Route, route.SnapModifier)),
            SelectSourcesSceneIntentV1 => throw new InvalidOperationException(
                "Selection must enter through the Web-owned selection callback."),
            _ => throw new InvalidOperationException("The Scene Intent variant is undefined."),
        };
    }

    private async Task ToggleProbeAsync(
        ToggleProbeSceneIntentV1 intent,
        CircuitDefinition definition)
    {
        var projection = Projection;
        if (projection?.Simulation is not { } simulation)
        {
            return;
        }
        var target = TranslateElaboratedNet(intent.Net, definition);
        var bindings = new List<ProbeBindingRequest>(simulation.Probes.Count + 1);
        var removed = false;
        foreach (var probe in simulation.Probes)
        {
            if (probe.Source == target)
            {
                removed = true;
                continue;
            }

            bindings.Add(new RetainProbe(probe.ProbeId, probe.Source));
        }

        if (!removed)
        {
            bindings.Add(new CreateProbe(target));
        }

        var precondition = SessionPrecondition();
        var outcome = await Execute(context => new ReplaceProbes(
            context,
            precondition,
            bindings));
        if (outcome is WorkspaceCommandRejected rejected)
        {
            Status = Text["SessionRejected", rejected.Code];
        }
    }

    private CompilationSource TranslateElaboratedNet(
        SceneElaboratedNetRefV1 reference,
        CircuitDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(reference);
        var net = ResolveNet(definition, reference.AuthoredNet);
        var document = Projection!.ProjectRevision.Document;
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

    private CircuitDefinition ResolveIntentDefinition(SceneIntentV1 intent)
    {
        var projection = Projection
            ?? throw new InvalidOperationException("The Workspace is not open.");
        if (projection.ProjectionVersion != intent.ProjectionVersion
            || SelectedDefinitionId is null
            || !string.Equals(
                SelectedDefinitionId.Value,
                intent.CircuitDefinitionId,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The Scene Intent is stale.");
        }

        return projection.ProjectRevision.Document.FindCircuitDefinition(SelectedDefinitionId)
            ?? throw new InvalidOperationException("The Scene Circuit Definition is missing.");
    }

    private ComponentTarget TranslateTarget(SceneComponentTargetV1 target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return target switch
        {
            SceneLibraryComponentTargetV1 library => new LibraryComponentTarget(
                new ComponentContractKey(library.LibraryId, library.ContractId)),
            SceneCircuitDefinitionTargetV1 definition =>
                new CircuitDefinitionComponentTarget(
                    Projection!.ProjectRevision.Document.CircuitDefinitions.Single(candidate =>
                        string.Equals(
                            candidate.Id.Value,
                            definition.CircuitDefinitionId,
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
                Projection!.ProjectRevision.Document.MemoryImages.Single(image =>
                    string.Equals(
                        image.Id.Value,
                        memory.MemoryImageId,
                        StringComparison.Ordinal)).Id),
            _ => throw new InvalidOperationException("The Scene parameter value is undefined."),
        };
        return new ComponentParameterBinding(binding.ParameterId, value);
    }

    private static ComponentPlacement TranslatePlacement(
        SceneComponentPlacementV1 placement,
        string snapModifier)
    {
        ArgumentNullException.ThrowIfNull(placement);
        ValidateSnapModifier(snapModifier);
        if (placement.QuarterTurnsClockwise is < 0 or > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(placement));
        }

        return new ComponentPlacement(
            TranslatePoint(placement.Origin, snapModifier),
            (QuarterTurn)placement.QuarterTurnsClockwise,
            placement.Reflected);
    }

    private static DefinitionPortPlacement TranslatePlacement(
        SceneDefinitionPortPlacementV1 placement,
        string snapModifier)
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
        return new DefinitionPortPlacement(
            TranslatePoint(placement.Position, snapModifier),
            facing);
    }

    private static GridPoint TranslatePoint(SceneGridPointV1 point, string snapModifier)
    {
        ArgumentNullException.ThrowIfNull(point);
        ValidateSnapModifier(snapModifier);
        return new GridPoint(point.X, point.Y);
    }

    private static WireRoute TranslateRoute(SceneWireRouteV1 route, string snapModifier)
    {
        ArgumentNullException.ThrowIfNull(route);
        ValidateSnapModifier(snapModifier);
        return route switch
        {
            SceneUnroutedWireRouteV1 => new UnroutedWireRoute(),
            SceneOrthogonalWireRouteV1 orthogonal => new OrthogonalWireRoute(
                [.. orthogonal.Points.Select(item => TranslatePoint(item, snapModifier))]),
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

    private static AuthoredTerminalReference TranslateTerminal(
        CircuitDefinition definition,
        SceneTerminalRefV1 terminal)
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

    private static WireGeometryReplacement TranslateReplacement(
        CircuitDefinition definition,
        SceneWireReplacementV1 replacement,
        string snapModifier)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        return new WireGeometryReplacement(
            ResolveWireGeometry(definition, replacement.WireGeometry).Id,
            TranslateRoute(replacement.Route, snapModifier));
    }

    private static JunctionRemovalPartition TranslatePartition(
        CircuitDefinition definition,
        SceneJunctionRemovalPartitionV1 partition,
        string snapModifier)
    {
        ArgumentNullException.ThrowIfNull(partition);
        ArgumentNullException.ThrowIfNull(partition.Membership);
        ArgumentNullException.ThrowIfNull(partition.Membership.Terminals);
        ArgumentNullException.ThrowIfNull(partition.Membership.Junctions);
        ArgumentNullException.ThrowIfNull(partition.Membership.WireGeometries);
        ArgumentNullException.ThrowIfNull(partition.RouteAdditions);
        return new JunctionRemovalPartition(
            new NetPartition(
                [.. partition.Membership.Terminals.Select(item =>
                    TranslateTerminal(definition, item))],
                [.. partition.Membership.Junctions.Select(item =>
                    ResolveJunction(definition, item).Id)],
                [.. partition.Membership.WireGeometries.Select(item =>
                    ResolveWireGeometry(definition, item).Id)]),
            [.. partition.RouteAdditions.Select(item =>
                TranslateRoute(item, snapModifier))]);
    }

    private static ComponentInstance ResolveComponent(
        CircuitDefinition definition,
        SceneSourceRefV1 source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return definition.ComponentInstances.Single(candidate =>
            IsSource(source, definition, "componentInstance", candidate.Id.Value));
    }

    private static DefinitionPort ResolveDefinitionPort(
        CircuitDefinition definition,
        SceneSourceRefV1 source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return definition.Ports.Single(candidate =>
            IsSource(source, definition, "definitionPort", candidate.Id.Value));
    }

    private static Annotation ResolveAnnotation(
        CircuitDefinition definition,
        SceneSourceRefV1 source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return definition.Annotations.Single(candidate =>
            IsSource(source, definition, "annotation", candidate.Id.Value));
    }

    private static Net ResolveNet(
        CircuitDefinition definition,
        SceneSourceRefV1 source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return definition.Nets.Single(candidate =>
            IsSource(source, definition, "net", candidate.Id.Value));
    }

    private static Junction ResolveJunction(
        CircuitDefinition definition,
        SceneSourceRefV1 source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return definition.Junctions.Single(candidate =>
            IsSource(source, definition, "junction", candidate.Id.Value));
    }

    private static WireGeometry ResolveWireGeometry(
        CircuitDefinition definition,
        SceneSourceRefV1 source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return definition.WireGeometries.Single(candidate =>
            IsSource(source, definition, "wireGeometry", candidate.Id.Value));
    }

    private static bool IsSource(
        SceneSourceRefV1 source,
        CircuitDefinition definition,
        string kind,
        string id) => string.Equals(
            source.CircuitDefinitionId,
            definition.Id.Value,
            StringComparison.Ordinal)
        && string.Equals(source.EntityKind, kind, StringComparison.Ordinal)
        && string.Equals(source.EntityId, id, StringComparison.Ordinal)
        && source.PortId is null;
}
