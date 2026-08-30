using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
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
        ulong waveformVersion,
        ulong? primaryCursor,
        ulong? secondaryCursor,
        bool liveFollow,
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
            radixByProbeId)).ToArray();
        var rows = MergeRecoveryRows(activeRows, recoveryRows);
        var browserViewport = Range(viewport);
        return new WaveformSnapshotV1(
            LogicLabWebBuild.Fingerprint,
            waveformVersion,
            projection.ProjectionVersion,
            simulation.SessionId.Value,
            simulation.SessionVersion,
            ArtifactKey(simulation.CompilationArtifactKey),
            UiCulture,
            "leftToRight",
            rows,
            new WaveformViewStateV1(
                browserViewport,
                primaryCursor is { } primary
                    ? new WaveformCursorV1(
                        "primary",
                        primary.ToString(CultureInfo.InvariantCulture))
                    : null,
                secondaryCursor is { } secondary
                    ? new WaveformCursorV1(
                        "secondary",
                        secondary.ToString(CultureInfo.InvariantCulture))
                    : null,
                liveFollow),
            ProjectTrace(rows, browserViewport, trace));
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

    private static string UiCulture => string.Equals(
        CultureInfo.CurrentUICulture.Name,
        "zh-CN",
        StringComparison.Ordinal)
        ? "zh-CN"
        : "en-US";

    private static string Part(string value) => string.Concat(
        value.Length.ToString(CultureInfo.InvariantCulture),
        ':',
        value);

    private static WaveformRowV1 Row(
        ProjectRevision revision,
        ProbeProjection probe,
        int displayOrdinal,
        IReadOnlyDictionary<string, string> radixByProbeId)
    {
        if (probe.Source.Identity is not NetSourceIdentity source)
        {
            throw new InvalidOperationException(
                "A Waveform Probe must identify an authored Net.");
        }

        var probeId = probe.ProbeId.Value;
        var appearance = AppearanceOrdinal(probeId);
        var sceneSource = SceneSourceMap.From(source);
        var sourceExists = SceneSourceMap.Contains(revision, sceneSource);
        var hasVisibleGeometry = sourceExists && HasVisibleGeometry(revision, source);
        var radix = radixByProbeId.TryGetValue(probeId, out var requestedRadix)
            ? requestedRadix
            : probe.Value.Count <= 4 ? "binary" : "hex";
        return new WaveformRowV1(
            probeId,
            new SceneElaboratedNetRefV1(
                sceneSource,
                new SceneHierarchyPathV1(
                    probe.Source.HierarchyPath.EntryCircuitDefinitionId.Value,
                    [.. probe.Source.HierarchyPath.Steps.Select(step =>
                        new SceneHierarchyStepV1(
                            step.ContainingCircuitDefinitionId.Value,
                            step.ComponentInstanceId.Value))])),
            checked((uint)probe.Value.Count),
            displayOrdinal,
            ProbePresentation.Label(revision, probe.Source, displayOrdinal),
            radix,
            appearance,
            Pattern(appearance),
            "resolved",
            bindingReason: null,
            hasVisibleGeometry ? "available" : "unavailable",
            hasVisibleGeometry
                ? null
                : sourceExists ? "noVisibleGeometry" : "sourceMissing",
            Value(probe.Value));
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
            .Select((row, ordinal) => row with { })
            .ToArray();
        if (merged.Select((row, ordinal) => row.DisplayOrdinal == ordinal).All(value => value))
        {
            return merged;
        }

        return [.. merged.Select((row, ordinal) => new WaveformRowV1(
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
            row.CurrentValue))];
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
                    bucket.HadMixedValues,
                    bucket.HadUnavailableValues))],
                gaps: [],
                summary.LatestSequence.ToString(CultureInfo.InvariantCulture)),
            TraceWindowUnavailable unavailable => new WaveformUnavailableViewV1(
                new WaveformTraceGapV1(
                    viewport,
                    unavailable.Reason switch
                    {
                        TraceWindowUnavailableReason.Evicted => "evicted",
                        TraceWindowUnavailableReason.ArtifactChanged => "artifactChanged",
                        _ => throw new InvalidOperationException(
                            "The Workspace Trace unavailable reason is undefined."),
                    }),
                unavailable.EarliestAvailable.ToString(CultureInfo.InvariantCulture),
                unavailable.LatestSequence.ToString(CultureInfo.InvariantCulture)),
            _ => throw new InvalidOperationException(
                "The Workspace Trace outcome is undefined."),
        };
    }

    private static WaveformTransitionsViewV1 ProjectTransitions(
        IReadOnlyList<WaveformRowV1> rows,
        WaveformTimeRangeV1 viewport,
        TraceTransitionsWindow trace)
    {
        var segments = new List<WaveformTransitionSegmentV1>();
        foreach (var row in rows.Where(row => row.Binding == "resolved"))
        {
            var transitions = trace.Transitions
                .Where(transition => string.Equals(
                    transition.ProbeId.Value,
                    row.ProbeId,
                    StringComparison.Ordinal))
                .OrderBy(transition => ulong.Parse(
                    transition.Sequence,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture))
                .ToArray();
            var baseline = transitions.LastOrDefault(transition => ulong.Parse(
                transition.LogicalTime,
                NumberStyles.None,
                CultureInfo.InvariantCulture) <= viewport.StartValue)
                ?? throw new ArgumentException(
                    "A transition viewport requires one baseline per resolved row.",
                    nameof(trace));
            var changes = transitions
                .Where(transition => ulong.Parse(
                    transition.LogicalTime,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture) > viewport.StartValue
                    && ulong.Parse(
                        transition.LogicalTime,
                        NumberStyles.None,
                        CultureInfo.InvariantCulture) < viewport.EndValue)
                .ToArray();
            var currentStart = viewport.StartValue;
            var current = baseline;
            foreach (var change in changes)
            {
                var changeTime = ulong.Parse(
                    change.LogicalTime,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture);
                segments.Add(new WaveformTransitionSegmentV1(
                    row.ProbeId,
                    new WaveformTimeRangeV1(
                        currentStart.ToString(CultureInfo.InvariantCulture),
                        changeTime.ToString(CultureInfo.InvariantCulture)),
                    current.Sequence,
                    Value(current.Value),
                    transitionAtStart: currentStart != viewport.StartValue));
                currentStart = changeTime;
                current = change;
            }

            segments.Add(new WaveformTransitionSegmentV1(
                row.ProbeId,
                new WaveformTimeRangeV1(
                    currentStart.ToString(CultureInfo.InvariantCulture),
                    viewport.EndExclusive),
                current.Sequence,
                Value(current.Value),
                transitionAtStart: currentStart != viewport.StartValue));
        }

        return new WaveformTransitionsViewV1(
            segments,
            gaps: [],
            trace.LatestSequence.ToString(CultureInfo.InvariantCulture));
    }

    private static WaveformTimeRangeV1 Range(TraceTimeRange range) => new(
        range.StartInclusive.ToString(CultureInfo.InvariantCulture),
        range.EndExclusive.ToString(CultureInfo.InvariantCulture));

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

    private static uint AppearanceOrdinal(string probeId)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(probeId));
        return BinaryPrimitives.ReadUInt32LittleEndian(digest) % 16U;
    }

    private static string Pattern(uint appearanceOrdinal) => (appearanceOrdinal % 4U) switch
    {
        0 => "solid",
        1 => "dash",
        2 => "dot",
        _ => "dashDot",
    };

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
}

internal static class ProbePresentation
{
    public static string Label(
        ProjectRevision revision,
        CompilationSource compilationSource,
        int ordinal)
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
            return output.DisplayName ?? "Output";
        }

        var inputPort = boundaryPorts.FirstOrDefault(port => port.Direction == PortDirection.Input);
        if (inputPort is not null)
        {
            return inputPort.DisplayName;
        }

        var input = components.FirstOrDefault(item =>
            item.Target!.ContractKey.ContractId == "source.input").Instance;
        return input is not null
            ? input.DisplayName ?? "Input"
            : FormattableString.Invariant($"P{ordinal + 1}");
    }
}
