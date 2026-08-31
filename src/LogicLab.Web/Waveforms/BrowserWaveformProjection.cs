using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using LogicLab.Application.Workspaces;
using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.Engine.Compilation;
using LogicLab.Web.Scene;

namespace LogicLab.Web.Waveforms;

internal static class BrowserWaveformProjection
{
    public static WaveformSnapshotV1 Create(
        WorkspaceProjection projection,
        TraceTimeRange viewport,
        TraceWindowOutcome trace,
        IReadOnlyDictionary<string, string> radixByProbeId,
        ProbePresentationLabels labels,
        ulong waveformVersion,
        ulong? primaryCursor,
        ulong? secondaryCursor,
        IReadOnlyList<WaveformRowV1>? recoveryRows = null)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(trace);
        ArgumentNullException.ThrowIfNull(radixByProbeId);
        var simulation = projection.Simulation
            ?? throw new ArgumentException(
                "A Waveform snapshot requires an active Simulation Session.",
                nameof(projection));
        var activeRows = simulation.Probes.Select((probe, ordinal) => Row(
            projection.ProjectRevision,
            probe,
            ordinal,
            radixByProbeId,
            labels)).ToArray();
        var rows = MergeRecoveryRows(activeRows, recoveryRows);
        var browserViewport = Range(viewport);
        return Snapshot(
            projection,
            simulation,
            rows,
            browserViewport,
            ProjectTrace(rows, browserViewport, trace),
            waveformVersion,
            primaryCursor,
            secondaryCursor);
    }

    public static WaveformSnapshotV1 CreateRecovery(
        WorkspaceProjection projection,
        TraceTimeRange viewport,
        ulong waveformVersion,
        ulong? primaryCursor,
        ulong? secondaryCursor,
        bool summary,
        IReadOnlyList<WaveformRowV1> recoveryRows)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(recoveryRows);
        var simulation = projection.Simulation
            ?? throw new ArgumentException(
                "A Waveform snapshot requires an active Simulation Session.",
                nameof(projection));
        if (simulation.Probes.Count != 0
            || recoveryRows.Count == 0
            || recoveryRows.Any(row => row.Binding != "unresolved"))
        {
            throw new ArgumentException(
                "A recovery Waveform requires only unresolved historical rows.",
                nameof(recoveryRows));
        }

        var rows = MergeRecoveryRows([], recoveryRows);
        var browserViewport = Range(viewport);
        WaveformTraceV1 trace = summary
            ? new WaveformSummaryViewV1(
                TraceVisualSummaryRequest.LogicEnvelopeV1,
                [])
            : new WaveformTransitionsViewV1([]);
        return Snapshot(
            projection,
            simulation,
            rows,
            browserViewport,
            trace,
            waveformVersion,
            primaryCursor,
            secondaryCursor);
    }

    private static WaveformSnapshotV1 Snapshot(
        WorkspaceProjection projection,
        SimulationProjection simulation,
        IReadOnlyList<WaveformRowV1> rows,
        WaveformTimeRangeV1 viewport,
        WaveformTraceV1 trace,
        ulong waveformVersion,
        ulong? primaryCursor,
        ulong? secondaryCursor)
    {
        return new WaveformSnapshotV1(
            LogicLabWebBuild.Fingerprint,
            waveformVersion,
            projection.ProjectionVersion,
            simulation.SessionId.Value,
            simulation.SessionVersion,
            ArtifactKey(simulation.CompilationArtifactKey),
            rows,
            new WaveformViewStateV1(
                viewport,
                primaryCursor is { } primary
                    ? new WaveformCursorV1(
                        "primary",
                        primary.ToString(CultureInfo.InvariantCulture))
                    : null,
                secondaryCursor is { } secondary
                    ? new WaveformCursorV1(
                        "secondary",
                        secondary.ToString(CultureInfo.InvariantCulture))
                    : null),
            trace);
    }

    public static string ArtifactKey(CompilationArtifactKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return string.Join(
            '|',
            Part(key.ProjectRevisionId.Value),
            Part(key.EntryCircuitDefinitionId.Value),
            Part(key.LibrarySnapshotFingerprint),
            Part(key.CompilerSemanticVersion));
    }

    private static string Part(string value) => string.Concat(
        value.Length.ToString(CultureInfo.InvariantCulture),
        ':',
        value);

    private static WaveformRowV1 Row(
        ProjectRevision revision,
        ProbeProjection probe,
        int displayOrdinal,
        IReadOnlyDictionary<string, string> radixByProbeId,
        ProbePresentationLabels labels)
    {
        if (probe.Source.Identity is not NetSourceIdentity source)
        {
            throw new InvalidOperationException(
                "A Waveform Probe must identify an authored Net.");
        }

        var probeId = probe.ProbeId.Value;
        var appearance = ProbeAppearanceV1.From(probeId);
        var sceneSource = SceneSourceMap.From(source);
        var sceneNet = new SceneElaboratedNetRefV1(
            sceneSource,
            new SceneHierarchyPathV1(
                probe.Source.HierarchyPath.EntryCircuitDefinitionId.Value,
                [.. probe.Source.HierarchyPath.Steps.Select(step =>
                    new SceneHierarchyStepV1(
                        step.ContainingCircuitDefinitionId.Value,
                        step.ComponentInstanceId.Value))]));
        var sourceExists = SceneSourceMap.Contains(revision, sceneSource);
        var sourceIsCurrent = TryResolveSource(revision, sceneNet, out var currentSource);
        var hasVisibleGeometry = currentSource?.Identity is NetSourceIdentity currentNet
            && HasVisibleGeometry(revision, currentNet);
        var radix = radixByProbeId.TryGetValue(probeId, out var requestedRadix)
            ? requestedRadix
            : probe.Value.Count <= 4 ? "binary" : "hex";
        return new WaveformRowV1(
            probeId,
            sceneNet,
            checked((uint)probe.Value.Count),
            displayOrdinal,
            ProbePresentation.Label(revision, probe.Source, displayOrdinal, labels),
            radix,
            appearance.Ordinal,
            appearance.Pattern,
            "resolved",
            bindingReason: null,
            hasVisibleGeometry ? "available" : "unavailable",
            hasVisibleGeometry
                ? null
                : !sourceExists
                    ? "sourceMissing"
                    : sourceIsCurrent ? "noVisibleGeometry" : "projectionUnavailable",
            Value(probe.Value));
    }

    internal static WaveformRowV1 Recover(
        ProjectRevision revision,
        WaveformRowV1 row,
        ProbePresentationLabels labels)
    {
        ArgumentNullException.ThrowIfNull(revision);
        ArgumentNullException.ThrowIfNull(row);
        var sourceExists = SceneSourceMap.Contains(revision, row.Net.AuthoredNet);
        var sourceIsCurrent = TryResolveSource(revision, row.Net, out var source);
        var hasVisibleGeometry = source?.Identity is NetSourceIdentity net
            && HasVisibleGeometry(revision, net);
        return new WaveformRowV1(
            row.ProbeId,
            row.Net,
            row.Width,
            row.DisplayOrdinal,
            source is not null
                ? ProbePresentation.Label(
                    revision,
                    source,
                    row.DisplayOrdinal,
                    labels)
                : row.ShortLabel,
            row.Radix,
            row.AppearanceOrdinal,
            row.Pattern,
            "unresolved",
            "artifactIncompatible",
            hasVisibleGeometry ? "available" : "unavailable",
            hasVisibleGeometry
                ? null
                : !sourceExists
                    ? "sourceMissing"
                    : sourceIsCurrent ? "noVisibleGeometry" : "projectionUnavailable",
            currentValue: null);
    }

    internal static bool TryResolveSource(
        ProjectRevision revision,
        WaveformRowV1 row,
        [NotNullWhen(true)] out CompilationSource? source)
    {
        ArgumentNullException.ThrowIfNull(row);
        return TryResolveSource(revision, row.Net, out source);
    }

    private static bool TryResolveSource(
        ProjectRevision revision,
        SceneElaboratedNetRefV1 net,
        [NotNullWhen(true)] out CompilationSource? source)
    {
        ArgumentNullException.ThrowIfNull(revision);
        ArgumentNullException.ThrowIfNull(net);
        source = null;
        var document = revision.Document;
        var definition = document.CircuitDefinitions.SingleOrDefault(candidate =>
            string.Equals(
                candidate.Id.Value,
                net.AuthoredNet.CircuitDefinitionId,
                StringComparison.Ordinal));
        if (definition is null)
        {
            return false;
        }

        try
        {
            source = new SceneIntentTranslator(document, definition).TranslateProbe(net);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException
            or InvalidOperationException)
        {
            return false;
        }
    }

    private static WaveformRowV1[] MergeRecoveryRows(
        IReadOnlyList<WaveformRowV1> activeRows,
        IReadOnlyList<WaveformRowV1>? recoveryRows)
    {
        var activeIds = activeRows.Select(row => row.ProbeId)
            .ToHashSet(StringComparer.Ordinal);
        var merged = activeRows
            .Concat((recoveryRows ?? []).Where(row =>
                !activeIds.Contains(row.ProbeId)
                && !activeRows.Any(active => SameSource(active.Net, row.Net))))
            .ToArray();
        return [.. merged.Select((row, ordinal) => row.DisplayOrdinal == ordinal
            ? row
            : WithDisplayOrdinal(row, ordinal))];
    }

    private static WaveformTraceV1 ProjectTrace(
        IReadOnlyList<WaveformRowV1> rows,
        WaveformTimeRangeV1 viewport,
        TraceWindowOutcome trace)
    {
        return trace switch
        {
            TraceTransitionsWindow transitions => ProjectTransitions(
                rows,
                viewport,
                transitions),
            TraceSummaryWindow summary => new WaveformSummaryViewV1(
                summary.Aggregation,
                [.. summary.Buckets.Select(bucket => new WaveformSummarySegmentV1(
                    bucket.ProbeId.Value,
                    Range(bucket.Range),
                    Value(bucket.FirstValue),
                    Value(bucket.LastValue),
                    bucket.HadTransition,
                    bucket.HadMixedValues))]),
            TraceWindowUnavailable => new WaveformUnavailableViewV1(
                new WaveformTraceGapV1(viewport)),
            _ => throw new InvalidOperationException(
                "The Workspace Trace outcome is undefined."),
        };
    }

    private static WaveformTransitionsViewV1 ProjectTransitions(
        IReadOnlyList<WaveformRowV1> rows,
        WaveformTimeRangeV1 viewport,
        TraceTransitionsWindow trace)
    {
        var resolvedRows = rows.Where(row => row.Binding == "resolved").ToArray();
        var transitionsByProbe = resolvedRows.ToDictionary(
            row => row.ProbeId,
            _ => new List<ProjectedTransition>(),
            StringComparer.Ordinal);
        foreach (var transition in trace.Transitions)
        {
            if (transitionsByProbe.TryGetValue(
                    transition.ProbeId.Value,
                    out var probeTransitions))
            {
                probeTransitions.Add(new ProjectedTransition(
                    transition,
                    ulong.Parse(
                        transition.LogicalTime,
                        NumberStyles.None,
                        CultureInfo.InvariantCulture)));
            }
        }

        var segments = new List<WaveformTransitionSegmentV1>();
        foreach (var row in resolvedRows)
        {
            var probeId = row.ProbeId;
            var transitions = transitionsByProbe[probeId];
            ProjectedTransition? baseline = null;
            foreach (var transition in transitions)
            {
                if ((UInt128)transition.LogicalTime <= viewport.StartValue)
                {
                    baseline = transition;
                }
            }

            var current = baseline
                ?? throw new ArgumentException(
                    "A transition viewport requires one baseline per resolved row.",
                    nameof(trace));
            var currentStart = viewport.StartValue;
            foreach (var change in transitions)
            {
                if ((UInt128)change.LogicalTime <= viewport.StartValue)
                {
                    continue;
                }

                if ((UInt128)change.LogicalTime >= viewport.EndValue)
                {
                    break;
                }

                var changeTime = (UInt128)change.LogicalTime;
                if (changeTime != currentStart)
                {
                    segments.Add(new WaveformTransitionSegmentV1(
                        probeId,
                        new WaveformTimeRangeV1(
                            currentStart.ToString(CultureInfo.InvariantCulture),
                            changeTime.ToString(CultureInfo.InvariantCulture)),
                        Value(current.Transfer.Value),
                        transitionAtStart: currentStart != viewport.StartValue));
                }

                currentStart = changeTime;
                current = change;
            }

            segments.Add(new WaveformTransitionSegmentV1(
                probeId,
                new WaveformTimeRangeV1(
                    currentStart.ToString(CultureInfo.InvariantCulture),
                    viewport.EndExclusive),
                Value(current.Transfer.Value),
                transitionAtStart: currentStart != viewport.StartValue));
        }

        return new WaveformTransitionsViewV1(segments);
    }

    private static WaveformTimeRangeV1 Range(TraceTimeRange range) => new(
        range.StartInclusive.ToString(CultureInfo.InvariantCulture),
        range.EndExclusive.ToString(CultureInfo.InvariantCulture));

    private static WaveformRowV1 WithDisplayOrdinal(WaveformRowV1 row, int ordinal) => new(
        row.ProbeId,
        row.Net,
        row.Width,
        ordinal,
        row.ShortLabel,
        row.Radix,
        row.AppearanceOrdinal,
        row.Pattern,
        row.Binding,
        row.BindingReason,
        row.SceneNavigation,
        row.NavigationReason,
        row.CurrentValue);

    private static WaveformLogicVectorV1 Value(LogicVectorTransferV1 value) => new(
        value.Width,
        value.Encoding,
        Convert.ToBase64String([.. value.Data]));

    private static WaveformLogicVectorV1 Value(ReadOnlyCollection<LogicValue> values)
    {
        var bytes = new byte[checked((values.Count + 3) / 4)];
        for (var index = 0; index < values.Count; index++)
        {
            bytes[index / 4] |= checked((byte)((byte)values[index] << ((index % 4) * 2)));
        }

        return new WaveformLogicVectorV1(
            checked((uint)values.Count),
            "logic4-2bit-v1",
            Convert.ToBase64String(bytes));
    }

    internal static bool MatchesSource(WaveformRowV1 row, CompilationSource source)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(source);
        if (source.Identity is not NetSourceIdentity net)
        {
            return false;
        }

        return string.Equals(
                row.Net.AuthoredNet.CircuitDefinitionId,
                net.CircuitDefinitionId.Value,
                StringComparison.Ordinal)
            && string.Equals(
                row.Net.AuthoredNet.EntityId,
                net.NetId.Value,
                StringComparison.Ordinal)
            && string.Equals(
                row.Net.HierarchyPath.EntryCircuitDefinitionId,
                source.HierarchyPath.EntryCircuitDefinitionId.Value,
                StringComparison.Ordinal)
            && row.Net.HierarchyPath.Steps.SequenceEqual(
                source.HierarchyPath.Steps.Select(step => new SceneHierarchyStepV1(
                    step.ContainingCircuitDefinitionId.Value,
                    step.ComponentInstanceId.Value)));
    }

    private static bool SameSource(
        SceneElaboratedNetRefV1 left,
        SceneElaboratedNetRefV1 right) =>
        left.AuthoredNet == right.AuthoredNet
        && string.Equals(
            left.HierarchyPath.EntryCircuitDefinitionId,
            right.HierarchyPath.EntryCircuitDefinitionId,
            StringComparison.Ordinal)
        && left.HierarchyPath.Steps.SequenceEqual(right.HierarchyPath.Steps);

    private static bool HasVisibleGeometry(ProjectRevision revision, NetSourceIdentity source)
    {
        var definition = revision.Document.FindCircuitDefinition(source.CircuitDefinitionId);
        var net = definition?.FindNet(source.NetId);
        return net is not null
            && (net.Terminals.Count != 0
                || definition!.Junctions.Any(junction => junction.NetId == net.Id)
                || definition.WireGeometries.Any(wire => wire.NetId == net.Id));
    }

    private readonly record struct ProjectedTransition(
        TraceTransitionTransfer Transfer,
        ulong LogicalTime);
}

internal static class ProbePresentation
{
    public static string Label(
        ProjectRevision revision,
        CompilationSource compilationSource,
        int ordinal,
        ProbePresentationLabels labels)
    {
        ArgumentNullException.ThrowIfNull(revision);
        ArgumentNullException.ThrowIfNull(compilationSource);
        if (compilationSource.Identity is not NetSourceIdentity source
            || revision.Document.FindCircuitDefinition(source.CircuitDefinitionId)
                is not { } definition
            || definition.FindNet(source.NetId) is not { } net)
        {
            return FormattableString.Invariant($"P{ordinal + 1}");
        }

        var boundaryPorts = net.Terminals
            .OfType<DefinitionTerminalReference>()
            .Select(terminal => definition.FindPort(terminal.DefinitionPortId))
            .OfType<DefinitionPort>()
            .ToArray();
        var netLabel = FormattableString.Invariant(
            $"N{definition.Nets.IndexOf(net) + 1}");
        var outputPort = boundaryPorts.FirstOrDefault(port => port.Direction == PortDirection.Output);
        if (outputPort is not null)
        {
            return outputPort.DisplayName;
        }

        var components = net.Terminals
            .OfType<InstanceTerminalReference>()
            .Select(terminal => definition.FindComponentInstance(terminal.ComponentInstanceId))
            .OfType<ComponentInstance>()
            .Select(instance => (Instance: instance, Target: instance.Target as LibraryComponentTarget))
            .Where(item => item.Target is not null)
            .ToArray();
        var output = components.FirstOrDefault(item =>
            item.Target!.ContractKey.ContractId == "sink.output").Instance;
        if (output is not null)
        {
            return output.DisplayName ?? labels.Output;
        }

        var inputPort = boundaryPorts.FirstOrDefault(port => port.Direction == PortDirection.Input);
        if (inputPort is not null)
        {
            return inputPort.DisplayName;
        }

        var input = components.FirstOrDefault(item =>
            item.Target!.ContractKey.ContractId == "source.input").Instance;
        return input is not null
            ? input.DisplayName ?? labels.Input
            : netLabel;
    }
}

internal readonly record struct ProbePresentationLabels(string Input, string Output);
