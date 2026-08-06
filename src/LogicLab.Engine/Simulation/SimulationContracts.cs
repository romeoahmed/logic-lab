using System.Collections.ObjectModel;
using LogicLab.Domain;
using LogicLab.Domain.Components;
using LogicLab.Engine.Compilation;

namespace LogicLab.Engine.Simulation;

public sealed record SimulationPolicyReference
{
    public SimulationPolicyReference(string policyId, string policyRevision)
    {
        ArgumentException.ThrowIfNullOrEmpty(policyId);
        ArgumentException.ThrowIfNullOrEmpty(policyRevision);
        PolicyIdentity.ValidateTokens("Simulation", policyId, policyRevision);
        PolicyId = policyId;
        PolicyRevision = policyRevision;
    }

    public string PolicyId { get; }

    public string PolicyRevision { get; }
}

public sealed record TracePolicyReference
{
    public TracePolicyReference(string policyId, string policyRevision)
    {
        ArgumentException.ThrowIfNullOrEmpty(policyId);
        ArgumentException.ThrowIfNullOrEmpty(policyRevision);
        PolicyIdentity.ValidateTokens("Trace", policyId, policyRevision);

        PolicyId = policyId;
        PolicyRevision = policyRevision;
    }

    public string PolicyId { get; }

    public string PolicyRevision { get; }
}

public sealed class SimulationSessionConfiguration
{
    public SimulationSessionConfiguration(
        SimulationPolicyReference simulationPolicy,
        TracePolicyReference tracePolicy,
        IReadOnlyList<CompilationSource> initialProbeBindings)
    {
        ArgumentNullException.ThrowIfNull(simulationPolicy);
        ArgumentNullException.ThrowIfNull(tracePolicy);
        ArgumentNullException.ThrowIfNull(initialProbeBindings);
        var ownedProbeBindings = initialProbeBindings.ToArray();

        if (ownedProbeBindings.Any(static source => source is null))
        {
            throw new ArgumentException(
                "Initial Probe bindings cannot contain null sources.",
                nameof(initialProbeBindings));
        }

        SimulationPolicy = simulationPolicy;
        TracePolicy = tracePolicy;
        InitialProbeBindings = Array.AsReadOnly(ownedProbeBindings);
    }

    public SimulationPolicyReference SimulationPolicy { get; }

    public TracePolicyReference TracePolicy { get; }

    public ReadOnlyCollection<CompilationSource> InitialProbeBindings { get; }
}

public sealed class OpenSimulationRequest
{
    public OpenSimulationRequest(
        CompilationArtifact compilationArtifact,
        SimulationSessionConfiguration configuration,
        SimulationPolicy simulationPolicy,
        TracePolicy tracePolicy)
    {
        ArgumentNullException.ThrowIfNull(compilationArtifact);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(simulationPolicy);
        ArgumentNullException.ThrowIfNull(tracePolicy);
        CompilationArtifact = compilationArtifact;
        Configuration = configuration;
        SimulationPolicy = simulationPolicy;
        TracePolicy = tracePolicy;
    }

    public CompilationArtifact CompilationArtifact { get; }

    public SimulationSessionConfiguration Configuration { get; }

    public SimulationPolicy SimulationPolicy { get; }

    public TracePolicy TracePolicy { get; }
}

public sealed record SimulationSessionId
{
    internal SimulationSessionId(string value)
    {
        Value = value;
    }

    public string Value { get; }

    internal static SimulationSessionId Create()
    {
        return new SimulationSessionId(Guid.NewGuid().ToString("N"));
    }
}

public sealed record ProbeId
{
    internal ProbeId(string value)
    {
        Value = value;
    }

    public string Value { get; }

    internal static ProbeId Create()
    {
        return new ProbeId(Guid.NewGuid().ToString("N"));
    }
}

public readonly record struct TraceCursor(
    ulong EarliestAvailableSequence,
    ulong LatestSequence);

public readonly record struct LogicalTimeRange
{
    public LogicalTimeRange(ulong startInclusive, ulong endExclusive)
    {
        if (startInclusive >= endExclusive)
        {
            throw new ArgumentException(
                "A Logical-time range must be nonempty and half-open.",
                nameof(endExclusive));
        }

        StartInclusive = startInclusive;
        EndExclusive = endExclusive;
    }

    public ulong StartInclusive { get; }

    public ulong EndExclusive { get; }
}

public enum SimulationFailureReason
{
    ZeroTimeOscillation,
    SimulationResourceLimit,
    SimulationCancelled,
    SimulationInfrastructureFailure,
    SimulationInternalDefect,
}

public enum SimulationWorkPolicy
{
    Simulation,
    Trace,
}

public sealed record SimulationWorkObservation(
    SimulationWorkPolicy Policy,
    string Dimension,
    ulong Observed);

public sealed record SimulationPolicyEvidence(
    string PolicyId,
    string PolicyRevision,
    string Dimension,
    ulong Observed);

public sealed class SimulationWorkEvidence
{
    internal SimulationWorkEvidence(
        CompilationArtifactKey compilationArtifactKey,
        SimulationPolicyReference simulationPolicy,
        TracePolicyReference tracePolicy,
        SimulationWorkObservation[] observedDimensions,
        SimulationWorkObservation? policyLimitBreach)
    {
        CompilationArtifactKey = compilationArtifactKey;
        SimulationPolicy = simulationPolicy;
        TracePolicy = tracePolicy;
        ObservedDimensions = Array.AsReadOnly(
            (SimulationWorkObservation[])observedDimensions.Clone());
        PolicyLimitBreach = policyLimitBreach;
    }

    public CompilationArtifactKey CompilationArtifactKey { get; }

    public SimulationPolicyReference SimulationPolicy { get; }

    public TracePolicyReference TracePolicy { get; }

    public ReadOnlyCollection<SimulationWorkObservation> ObservedDimensions { get; }

    public SimulationWorkObservation? PolicyLimitBreach { get; }
}

public enum SimulationDiagnosticSeverity
{
    Info,
    Warning,
    Error,
}

public abstract record SimulationDiagnosticValue
{
    private protected SimulationDiagnosticValue()
    {
    }
}

public sealed record SimulationStableTokenValue(string Value)
    : SimulationDiagnosticValue;

public sealed record SimulationUnsignedDecimalValue(ulong Value)
    : SimulationDiagnosticValue;

public sealed record SimulationLogicValue(LogicValue Value)
    : SimulationDiagnosticValue;

public sealed record SimulationContractKeyValue(ComponentContractKey Value)
    : SimulationDiagnosticValue;

public sealed record SimulationCorrelationTokenValue(string Value)
    : SimulationDiagnosticValue;

public sealed record SimulationDiagnosticArgument(
    string Name,
    SimulationDiagnosticValue Value);

public sealed class SimulationDiagnostic
{
    internal SimulationDiagnostic(
        string code,
        SimulationDiagnosticSeverity severity,
        SimulationDiagnosticArgument[] arguments,
        CompilationSource? primary,
        CompilationSource[] related)
    {
        Code = code;
        Severity = severity;
        Arguments = Array.AsReadOnly(
            (SimulationDiagnosticArgument[])arguments.Clone());
        Primary = primary;
        Related = Array.AsReadOnly((CompilationSource[])related.Clone());
    }

    public string Code { get; }

    public SimulationDiagnosticSeverity Severity { get; }

    public ReadOnlyCollection<SimulationDiagnosticArgument> Arguments { get; }

    public CompilationSource? Primary { get; }

    public ReadOnlyCollection<CompilationSource> Related { get; }
}

public abstract record SimulationOpenOutcome
{
    private protected SimulationOpenOutcome()
    {
    }
}

public sealed record SimulationOpened : SimulationOpenOutcome
{
    internal SimulationOpened(
        SimulationSessionHandle handle,
        SimulationSessionId sessionId,
        ulong sessionVersion,
        CompilationArtifactKey compilationArtifactKey,
        ulong logicalTime,
        ProbeId[] probeIds,
        TraceCursor traceCursor,
        SimulationDiagnostic[] diagnostics,
        SimulationWorkEvidence workEvidence)
    {
        Handle = handle;
        SessionId = sessionId;
        SessionVersion = sessionVersion;
        CompilationArtifactKey = compilationArtifactKey;
        LogicalTime = logicalTime;
        ProbeIds = Array.AsReadOnly((ProbeId[])probeIds.Clone());
        TraceCursor = traceCursor;
        Diagnostics = Array.AsReadOnly((SimulationDiagnostic[])diagnostics.Clone());
        WorkEvidence = workEvidence;
    }

    public SimulationSessionHandle Handle { get; }

    public SimulationSessionId SessionId { get; }

    public ulong SessionVersion { get; }

    public CompilationArtifactKey CompilationArtifactKey { get; }

    public ulong LogicalTime { get; }

    public ReadOnlyCollection<ProbeId> ProbeIds { get; }

    public TraceCursor TraceCursor { get; }

    public ReadOnlyCollection<SimulationDiagnostic> Diagnostics { get; }

    public SimulationWorkEvidence WorkEvidence { get; }
}

public enum InitialProbeBindingInvalidRule
{
    UnresolvedSource,
    DuplicateResolvedNet,
}

public sealed record InitialProbeBindingsInvalid : SimulationOpenOutcome
{
    internal InitialProbeBindingsInvalid(
        InitialProbeBindingInvalidRule rule,
        int bindingIndex,
        int? conflictingBindingIndex,
        SimulationDiagnostic[] diagnostics,
        SimulationWorkEvidence workEvidence)
    {
        Rule = rule;
        BindingIndex = bindingIndex;
        ConflictingBindingIndex = conflictingBindingIndex;
        Diagnostics = Array.AsReadOnly((SimulationDiagnostic[])diagnostics.Clone());
        WorkEvidence = workEvidence;
    }

    public InitialProbeBindingInvalidRule Rule { get; }

    public int BindingIndex { get; }

    public int? ConflictingBindingIndex { get; }

    public ReadOnlyCollection<SimulationDiagnostic> Diagnostics { get; }

    public SimulationWorkEvidence WorkEvidence { get; }
}

public sealed record SimulationOpenRejected : SimulationOpenOutcome
{
    internal SimulationOpenRejected(
        SimulationFailureReason reason,
        SimulationDiagnostic[] diagnostics,
        SimulationWorkEvidence workEvidence)
    {
        Reason = reason;
        Diagnostics = Array.AsReadOnly((SimulationDiagnostic[])diagnostics.Clone());
        WorkEvidence = workEvidence;
    }

    public SimulationFailureReason Reason { get; }

    public ReadOnlyCollection<SimulationDiagnostic> Diagnostics { get; }

    public SimulationWorkEvidence WorkEvidence { get; }
}

public sealed record StimulusAssignment
{
    public StimulusAssignment(
        CompilationSource driverSource,
        LogicVector value)
    {
        ArgumentNullException.ThrowIfNull(driverSource);
        ArgumentNullException.ThrowIfNull(value);
        DriverSource = driverSource;
        Value = value;
    }

    public CompilationSource DriverSource { get; }

    public LogicVector Value { get; }
}

public sealed class StimulusBatch
{
    public StimulusBatch(
        ulong logicalTime,
        IReadOnlyList<StimulusAssignment> assignments)
    {
        ArgumentNullException.ThrowIfNull(assignments);
        var ownedAssignments = assignments.ToArray();
        if (ownedAssignments.Length == 0
            || ownedAssignments.Any(static item => item is null))
        {
            throw new ArgumentException(
                "A Stimulus Batch requires nonempty Driver assignments.",
                nameof(assignments));
        }

        LogicalTime = logicalTime;
        Assignments = Array.AsReadOnly(ownedAssignments);
    }

    public ulong LogicalTime { get; }

    public ReadOnlyCollection<StimulusAssignment> Assignments { get; }
}

public abstract record SimulationCommand
{
    private protected SimulationCommand()
    {
    }
}

public sealed record ScheduleStimulusBatch : SimulationCommand
{
    public ScheduleStimulusBatch(StimulusBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        Batch = batch;
    }

    public StimulusBatch Batch { get; }
}

public sealed record AdvanceToNextQuiescentBoundary : SimulationCommand;

public sealed record HotSwapSimulation : SimulationCommand
{
    public HotSwapSimulation(CompilationArtifact compilationArtifact)
    {
        ArgumentNullException.ThrowIfNull(compilationArtifact);
        CompilationArtifact = compilationArtifact;
    }

    public CompilationArtifact CompilationArtifact { get; }
}

public enum StimulusBatchInvalidRule
{
    AtOrBeforeCommittedTime,
    ConflictingDriverAssignment,
}

public sealed record ProbeObservation(
    ProbeId ProbeId,
    CompilationSource Source,
    LogicVector Value);

public abstract record SimulationCommandOutcome
{
    private protected SimulationCommandOutcome()
    {
    }
}

public sealed record StimulusBatchScheduled(
    ulong SessionVersion,
    ulong ScheduledLogicalTime,
    ulong StableSequence) : SimulationCommandOutcome;

public sealed record StimulusBatchInvalid(
    ulong SessionVersion,
    ulong LogicalTime,
    StimulusBatchInvalidRule Rule) : SimulationCommandOutcome;

public sealed record AdvanceCommitted : SimulationCommandOutcome
{
    internal AdvanceCommitted(
        ulong sessionVersion,
        ulong logicalTime,
        ProbeObservation[] observedProbePatch,
        SimulationDiagnostic[] diagnostics,
        TraceCursor traceCursor)
    {
        SessionVersion = sessionVersion;
        LogicalTime = logicalTime;
        ObservedProbePatch = Array.AsReadOnly(
            (ProbeObservation[])observedProbePatch.Clone());
        Diagnostics = Array.AsReadOnly((SimulationDiagnostic[])diagnostics.Clone());
        TraceCursor = traceCursor;
    }

    public ulong SessionVersion { get; }

    public ulong LogicalTime { get; }

    public ReadOnlyCollection<ProbeObservation> ObservedProbePatch { get; }

    public ReadOnlyCollection<SimulationDiagnostic> Diagnostics { get; }

    public TraceCursor TraceCursor { get; }
}

public sealed record NoScheduledStimulus(
    ulong SessionVersion,
    ulong LogicalTime) : SimulationCommandOutcome;

public sealed class HotSwapMigrationEvidence
{
    internal HotSwapMigrationEvidence(
        CompilationSource[] migratedStateSources,
        ProbeId[] preservedProbeIds,
        ProbeId[] unresolvedProbeIds)
    {
        MigratedStateSources = Array.AsReadOnly(
            (CompilationSource[])migratedStateSources.Clone());
        PreservedProbeIds = Array.AsReadOnly((ProbeId[])preservedProbeIds.Clone());
        UnresolvedProbeIds = Array.AsReadOnly((ProbeId[])unresolvedProbeIds.Clone());
    }

    public ReadOnlyCollection<CompilationSource> MigratedStateSources { get; }

    public ReadOnlyCollection<ProbeId> PreservedProbeIds { get; }

    public ReadOnlyCollection<ProbeId> UnresolvedProbeIds { get; }
}

public sealed record HotSwapCommitted : SimulationCommandOutcome
{
    internal HotSwapCommitted(
        ulong sessionVersion,
        CompilationArtifactKey compilationArtifactKey,
        HotSwapMigrationEvidence migrationEvidence,
        ProbeId[] probeIds,
        ProbeObservation[] observedProbes,
        SimulationDiagnostic[] diagnostics,
        TraceCursor traceCursor)
    {
        SessionVersion = sessionVersion;
        CompilationArtifactKey = compilationArtifactKey;
        MigrationEvidence = migrationEvidence;
        ProbeIds = Array.AsReadOnly((ProbeId[])probeIds.Clone());
        ObservedProbes = Array.AsReadOnly((ProbeObservation[])observedProbes.Clone());
        Diagnostics = Array.AsReadOnly((SimulationDiagnostic[])diagnostics.Clone());
        TraceCursor = traceCursor;
    }

    public ulong SessionVersion { get; }

    public CompilationArtifactKey CompilationArtifactKey { get; }

    public HotSwapMigrationEvidence MigrationEvidence { get; }

    public ReadOnlyCollection<ProbeId> ProbeIds { get; }

    public ReadOnlyCollection<ProbeObservation> ObservedProbes { get; }

    public ReadOnlyCollection<SimulationDiagnostic> Diagnostics { get; }

    public TraceCursor TraceCursor { get; }
}

public sealed record HotSwapIncompatible : SimulationCommandOutcome
{
    internal HotSwapIncompatible(
        ulong sessionVersion,
        CompilationArtifactKey compilationArtifactKey,
        CompilationSource[] incompatibleStateSources,
        ProbeId[] unresolvedProbeIds)
    {
        SessionVersion = sessionVersion;
        CompilationArtifactKey = compilationArtifactKey;
        IncompatibleStateSources = Array.AsReadOnly(
            (CompilationSource[])incompatibleStateSources.Clone());
        UnresolvedProbeIds = Array.AsReadOnly((ProbeId[])unresolvedProbeIds.Clone());
    }

    public ulong SessionVersion { get; }

    public CompilationArtifactKey CompilationArtifactKey { get; }

    public ReadOnlyCollection<CompilationSource> IncompatibleStateSources { get; }

    public ReadOnlyCollection<ProbeId> UnresolvedProbeIds { get; }
}

public sealed record AdvanceFailed : SimulationCommandOutcome
{
    internal AdvanceFailed(
        ulong sessionVersion,
        ulong logicalTime,
        SimulationFailureReason reason,
        SimulationDiagnostic[] diagnostics,
        SimulationPolicyEvidence? policyEvidence)
    {
        SessionVersion = sessionVersion;
        LogicalTime = logicalTime;
        Reason = reason;
        Diagnostics = Array.AsReadOnly((SimulationDiagnostic[])diagnostics.Clone());
        PolicyEvidence = policyEvidence;
    }

    public ulong SessionVersion { get; }

    public ulong LogicalTime { get; }

    public SimulationFailureReason Reason { get; }

    public ReadOnlyCollection<SimulationDiagnostic> Diagnostics { get; }

    public SimulationPolicyEvidence? PolicyEvidence { get; }
}

public sealed record SimulationCommandFailed : SimulationCommandOutcome
{
    internal SimulationCommandFailed(
        ulong sessionVersion,
        ulong logicalTime,
        SimulationFailureReason reason,
        SimulationDiagnostic[] diagnostics,
        SimulationPolicyEvidence? policyEvidence)
    {
        SessionVersion = sessionVersion;
        LogicalTime = logicalTime;
        Reason = reason;
        Diagnostics = Array.AsReadOnly((SimulationDiagnostic[])diagnostics.Clone());
        PolicyEvidence = policyEvidence;
    }

    public ulong SessionVersion { get; }

    public ulong LogicalTime { get; }

    public SimulationFailureReason Reason { get; }

    public ReadOnlyCollection<SimulationDiagnostic> Diagnostics { get; }

    public SimulationPolicyEvidence? PolicyEvidence { get; }
}

public abstract record CloseSimulationOutcome
{
    private protected CloseSimulationOutcome()
    {
    }
}

public sealed record SessionClosed(
    SimulationSessionId SessionId) : CloseSimulationOutcome;

public sealed record SessionAlreadyClosed(
    SimulationSessionId SessionId) : CloseSimulationOutcome;

public abstract record SimulationQuery
{
    private protected SimulationQuery()
    {
    }
}

public sealed record ReadSessionSnapshot : SimulationQuery;

public sealed record ReadTraceWindow : SimulationQuery
{
    public ReadTraceWindow(SimulationTraceWindowRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Request = request;
    }

    public SimulationTraceWindowRequest Request { get; }
}

public sealed class SimulationTraceWindowRequest
{
    public SimulationTraceWindowRequest(
        IReadOnlyList<ProbeId> probeIds,
        LogicalTimeRange range,
        ulong? afterSequence)
    {
        ArgumentNullException.ThrowIfNull(probeIds);
        var ownedProbeIds = probeIds.ToArray();
        if (ownedProbeIds.Length == 0
            || ownedProbeIds.Any(static id => id is null)
            || ownedProbeIds.Distinct().Count() != ownedProbeIds.Length)
        {
            throw new ArgumentException(
                "A Trace window requires nonempty unique ordered Probe IDs.",
                nameof(probeIds));
        }

        ProbeIds = Array.AsReadOnly(ownedProbeIds);
        Range = range;
        AfterSequence = afterSequence;
    }

    public ReadOnlyCollection<ProbeId> ProbeIds { get; }

    public LogicalTimeRange Range { get; }

    public ulong? AfterSequence { get; }
}

public sealed record ProbeSnapshot(
    ProbeId ProbeId,
    CompilationSource Source,
    LogicVector Value);

public sealed record TraceTransition(
    ulong Sequence,
    ProbeId ProbeId,
    ulong LogicalTime,
    LogicVector Value);

public abstract record SimulationReadOutcome
{
    private protected SimulationReadOutcome()
    {
    }
}

public sealed record SessionSnapshotRead : SimulationReadOutcome
{
    internal SessionSnapshotRead(
        SimulationSessionId sessionId,
        ulong sessionVersion,
        CompilationArtifactKey compilationArtifactKey,
        ulong logicalTime,
        ProbeSnapshot[] probes,
        TraceCursor traceCursor,
        SimulationDiagnostic[] diagnostics)
    {
        SessionId = sessionId;
        SessionVersion = sessionVersion;
        CompilationArtifactKey = compilationArtifactKey;
        LogicalTime = logicalTime;
        Probes = Array.AsReadOnly((ProbeSnapshot[])probes.Clone());
        TraceCursor = traceCursor;
        Diagnostics = Array.AsReadOnly((SimulationDiagnostic[])diagnostics.Clone());
    }

    public SimulationSessionId SessionId { get; }

    public ulong SessionVersion { get; }

    public CompilationArtifactKey CompilationArtifactKey { get; }

    public ulong LogicalTime { get; }

    public ReadOnlyCollection<ProbeSnapshot> Probes { get; }

    public TraceCursor TraceCursor { get; }

    public ReadOnlyCollection<SimulationDiagnostic> Diagnostics { get; }
}

public sealed record TraceTransitionsAvailable : SimulationReadOutcome
{
    internal TraceTransitionsAvailable(
        TraceTransition[] transitions,
        LogicalTimeRange coveredRange,
        ulong earliestAvailable,
        ulong latestSequence)
    {
        Transitions = Array.AsReadOnly((TraceTransition[])transitions.Clone());
        CoveredRange = coveredRange;
        EarliestAvailable = earliestAvailable;
        LatestSequence = latestSequence;
    }

    public ReadOnlyCollection<TraceTransition> Transitions { get; }

    public LogicalTimeRange CoveredRange { get; }

    public ulong EarliestAvailable { get; }

    public ulong LatestSequence { get; }
}

public enum TraceRangeUnavailableReason
{
    Evicted,
    ArtifactChanged,
}

public sealed record TraceRangeUnavailable : SimulationReadOutcome
{
    internal TraceRangeUnavailable(
        TraceRangeUnavailableReason reason,
        ulong earliestAvailable,
        ulong latestSequence)
    {
        Reason = reason;
        EarliestAvailable = earliestAvailable;
        LatestSequence = latestSequence;
    }

    public TraceRangeUnavailableReason Reason { get; }

    public ulong EarliestAvailable { get; }

    public ulong LatestSequence { get; }
}

public sealed record SimulationReadFailed : SimulationReadOutcome
{
    internal SimulationReadFailed(
        SimulationFailureReason reason,
        SimulationDiagnostic[] diagnostics)
    {
        Reason = reason;
        Diagnostics = Array.AsReadOnly((SimulationDiagnostic[])diagnostics.Clone());
    }

    public SimulationFailureReason Reason { get; }

    public ReadOnlyCollection<SimulationDiagnostic> Diagnostics { get; }
}
