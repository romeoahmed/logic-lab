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
        return new SimulationSessionId(Guid.CreateVersion7().ToString("N"));
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
        return new ProbeId(Guid.CreateVersion7().ToString("N"));
    }
}

public readonly record struct TraceCursor(
    ulong EarliestAvailableSequence,
    ulong LatestSequence);

public readonly record struct LogicalTimeRange
{
    public LogicalTimeRange(UInt128 startInclusive, UInt128 endExclusive)
    {
        if (startInclusive >= endExclusive || endExclusive > DomainEndExclusive)
        {
            throw new ArgumentException(
                "A Logical-time range must be nonempty, half-open, and within the u64 domain.",
                nameof(endExclusive));
        }

        StartInclusive = startInclusive;
        EndExclusive = endExclusive;
    }

    public static UInt128 DomainEndExclusive { get; } =
        (UInt128)ulong.MaxValue + UInt128.One;

    public UInt128 StartInclusive { get; }

    public UInt128 EndExclusive { get; }
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
        SimulationWorkObservation[] ownedObservedDimensions,
        SimulationWorkObservation? policyLimitBreach)
    {
        CompilationArtifactKey = compilationArtifactKey;
        SimulationPolicy = simulationPolicy;
        TracePolicy = tracePolicy;
        ObservedDimensions = Array.AsReadOnly(ownedObservedDimensions);
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
        SimulationDiagnosticArgument[] ownedArguments,
        CompilationSource? primary,
        CompilationSource[] ownedRelated)
    {
        Code = code;
        Severity = severity;
        Arguments = Array.AsReadOnly(ownedArguments);
        Primary = primary;
        Related = Array.AsReadOnly(ownedRelated);
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
        ProbeId[] ownedProbeIds,
        TraceCursor traceCursor,
        SimulationDiagnostic[] ownedDiagnostics,
        SimulationWorkEvidence workEvidence)
    {
        Handle = handle;
        SessionId = sessionId;
        SessionVersion = sessionVersion;
        CompilationArtifactKey = compilationArtifactKey;
        LogicalTime = logicalTime;
        ProbeIds = Array.AsReadOnly(ownedProbeIds);
        TraceCursor = traceCursor;
        Diagnostics = Array.AsReadOnly(ownedDiagnostics);
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
        SimulationDiagnostic[] ownedDiagnostics,
        SimulationWorkEvidence workEvidence)
    {
        Rule = rule;
        BindingIndex = bindingIndex;
        ConflictingBindingIndex = conflictingBindingIndex;
        Diagnostics = Array.AsReadOnly(ownedDiagnostics);
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
        SimulationDiagnostic[] ownedDiagnostics,
        SimulationWorkEvidence workEvidence)
    {
        Reason = reason;
        Diagnostics = Array.AsReadOnly(ownedDiagnostics);
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

public abstract record ProbeBindingRequest
{
    private protected ProbeBindingRequest(CompilationSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        Source = source;
    }

    public CompilationSource Source { get; }
}

public sealed record RetainProbe : ProbeBindingRequest
{
    public RetainProbe(ProbeId probeId, CompilationSource source)
        : base(source)
    {
        ArgumentNullException.ThrowIfNull(probeId);
        ProbeId = probeId;
    }

    public ProbeId ProbeId { get; }
}

public sealed record CreateProbe : ProbeBindingRequest
{
    public CreateProbe(CompilationSource source)
        : base(source)
    {
    }
}

public sealed record ReplaceProbeBindings : SimulationCommand
{
    public ReplaceProbeBindings(IReadOnlyList<ProbeBindingRequest> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        var ownedBindings = bindings.ToArray();
        if (ownedBindings.Any(static binding => binding is null))
        {
            throw new ArgumentException(
                "Probe bindings cannot contain null requests.",
                nameof(bindings));
        }

        Bindings = Array.AsReadOnly(ownedBindings);
    }

    public ReadOnlyCollection<ProbeBindingRequest> Bindings { get; }
}

public sealed record HotSwapConsumerBufferRequirements
{
    public HotSwapConsumerBufferRequirements(
        ulong retainedOwnedBufferBytes,
        ulong ownedReferenceSlotsPerObservedProbe,
        ulong ownedBytesPerObservedProbeBit)
    {
        RetainedOwnedBufferBytes = retainedOwnedBufferBytes;
        OwnedReferenceSlotsPerObservedProbe = ownedReferenceSlotsPerObservedProbe;
        OwnedBytesPerObservedProbeBit = ownedBytesPerObservedProbeBit;
    }

    public ulong RetainedOwnedBufferBytes { get; }

    public ulong OwnedReferenceSlotsPerObservedProbe { get; }

    public ulong OwnedBytesPerObservedProbeBit { get; }

    public static HotSwapConsumerBufferRequirements None { get; } = new(0, 0, 0);
}

public sealed record HotSwapTo : SimulationCommand
{
    public HotSwapTo(
        CompilationArtifact compilationArtifact,
        ulong maximumPeakOwnedBufferBytes,
        HotSwapConsumerBufferRequirements consumerBuffers)
    {
        ArgumentNullException.ThrowIfNull(compilationArtifact);
        ArgumentNullException.ThrowIfNull(consumerBuffers);
        ArgumentOutOfRangeException.ThrowIfZero(maximumPeakOwnedBufferBytes);
        CompilationArtifact = compilationArtifact;
        MaximumPeakOwnedBufferBytes = maximumPeakOwnedBufferBytes;
        ConsumerBuffers = consumerBuffers;
    }

    public CompilationArtifact CompilationArtifact { get; }

    public ulong MaximumPeakOwnedBufferBytes { get; }

    public HotSwapConsumerBufferRequirements ConsumerBuffers { get; }
}

public enum StimulusBatchInvalidRule
{
    AtOrBeforeCommittedTime,
    ConflictingDriverAssignment,
    DriverSourceUnresolved,
    DriverNotExternalInput,
    DriverWidthMismatch,
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
        ProbeObservation[] ownedObservedProbePatch,
        SimulationDiagnostic[] ownedDiagnostics,
        TraceCursor traceCursor)
    {
        SessionVersion = sessionVersion;
        LogicalTime = logicalTime;
        ObservedProbePatch = Array.AsReadOnly(ownedObservedProbePatch);
        Diagnostics = Array.AsReadOnly(ownedDiagnostics);
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

public sealed record ProbeBindingsReplaced : SimulationCommandOutcome
{
    internal ProbeBindingsReplaced(
        ulong sessionVersion,
        ProbeId[] ownedProbeIds,
        ProbeObservation[] ownedObservedProbes,
        TraceCursor traceCursor)
    {
        SessionVersion = sessionVersion;
        ProbeIds = Array.AsReadOnly(ownedProbeIds);
        ObservedProbes = Array.AsReadOnly(ownedObservedProbes);
        TraceCursor = traceCursor;
    }

    public ulong SessionVersion { get; }

    public ReadOnlyCollection<ProbeId> ProbeIds { get; }

    public ReadOnlyCollection<ProbeObservation> ObservedProbes { get; }

    public TraceCursor TraceCursor { get; }
}

public enum ProbeBindingsInvalidRule
{
    DuplicateBinding,
    UnresolvedSource,
    ArtifactMismatch,
}

public sealed record ProbeBindingsInvalid : SimulationCommandOutcome
{
    internal ProbeBindingsInvalid(
        ulong sessionVersion,
        ProbeBindingsInvalidRule rule,
        CompilationSource[] ownedSourceLocations)
    {
        SessionVersion = sessionVersion;
        Rule = rule;
        SourceLocations = Array.AsReadOnly(ownedSourceLocations);
    }

    public ulong SessionVersion { get; }

    public ProbeBindingsInvalidRule Rule { get; }

    public ReadOnlyCollection<CompilationSource> SourceLocations { get; }
}

public sealed class HotSwapMigrationEvidence
{
    internal HotSwapMigrationEvidence(
        CompilationSource[] ownedMigratedStateSources,
        ProbeId[] ownedPreservedProbeIds,
        ProbeId[] ownedUnresolvedProbeIds)
    {
        MigratedStateSources = Array.AsReadOnly(ownedMigratedStateSources);
        PreservedProbeIds = Array.AsReadOnly(ownedPreservedProbeIds);
        UnresolvedProbeIds = Array.AsReadOnly(ownedUnresolvedProbeIds);
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
        ProbeId[] ownedProbeIds,
        ProbeObservation[] ownedObservedProbes,
        SimulationDiagnostic[] ownedDiagnostics,
        TraceCursor traceCursor)
    {
        SessionVersion = sessionVersion;
        CompilationArtifactKey = compilationArtifactKey;
        MigrationEvidence = migrationEvidence;
        ProbeIds = Array.AsReadOnly(ownedProbeIds);
        ObservedProbes = Array.AsReadOnly(ownedObservedProbes);
        Diagnostics = Array.AsReadOnly(ownedDiagnostics);
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
        CompilationSource[] ownedIncompatibleStateSources,
        ProbeId[] ownedUnresolvedProbeIds)
    {
        SessionVersion = sessionVersion;
        CompilationArtifactKey = compilationArtifactKey;
        IncompatibleStateSources = Array.AsReadOnly(ownedIncompatibleStateSources);
        UnresolvedProbeIds = Array.AsReadOnly(ownedUnresolvedProbeIds);
    }

    public ulong SessionVersion { get; }

    public CompilationArtifactKey CompilationArtifactKey { get; }

    public ReadOnlyCollection<CompilationSource> IncompatibleStateSources { get; }

    public ReadOnlyCollection<ProbeId> UnresolvedProbeIds { get; }
}

public sealed record HotSwapResourceLimitExceeded : SimulationCommandOutcome
{
    internal HotSwapResourceLimitExceeded(
        ulong sessionVersion,
        CompilationArtifactKey compilationArtifactKey,
        ulong maximumPeakOwnedBufferBytes,
        ulong observedPeakOwnedBufferBytes)
    {
        ArgumentNullException.ThrowIfNull(compilationArtifactKey);
        SessionVersion = sessionVersion;
        CompilationArtifactKey = compilationArtifactKey;
        MaximumPeakOwnedBufferBytes = maximumPeakOwnedBufferBytes;
        ObservedPeakOwnedBufferBytes = observedPeakOwnedBufferBytes;
    }

    public ulong SessionVersion { get; }

    public CompilationArtifactKey CompilationArtifactKey { get; }

    public ulong MaximumPeakOwnedBufferBytes { get; }

    public ulong ObservedPeakOwnedBufferBytes { get; }
}

public sealed record AdvanceFailed : SimulationCommandOutcome
{
    internal AdvanceFailed(
        ulong sessionVersion,
        ulong logicalTime,
        SimulationFailureReason reason,
        SimulationDiagnostic[] ownedDiagnostics,
        SimulationPolicyEvidence? policyEvidence)
    {
        SessionVersion = sessionVersion;
        LogicalTime = logicalTime;
        Reason = reason;
        Diagnostics = Array.AsReadOnly(ownedDiagnostics);
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
        SimulationDiagnostic[] ownedDiagnostics,
        SimulationPolicyEvidence? policyEvidence)
    {
        SessionVersion = sessionVersion;
        LogicalTime = logicalTime;
        Reason = reason;
        Diagnostics = Array.AsReadOnly(ownedDiagnostics);
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
        TraceWindowRepresentation representation,
        ulong? afterSequence)
    {
        ArgumentNullException.ThrowIfNull(probeIds);
        ArgumentNullException.ThrowIfNull(representation);
        // A default value type bypasses LogicalTimeRange's constructor.
        if (range.StartInclusive >= range.EndExclusive)
        {
            throw new ArgumentException(
                "A Trace window range must be nonempty.", nameof(range));
        }

        if (representation is TraceVisualSummaryRepresentation
            && afterSequence is not null)
        {
            throw new ArgumentException(
                "A visual Trace summary does not accept a continuation sequence.",
                nameof(afterSequence));
        }

        var ownedProbeIds = probeIds.ToArray();
        if (ownedProbeIds.Length == 0
            || ownedProbeIds.Any(static id => id is null)
            || ownedProbeIds.Distinct().Count() != ownedProbeIds.Length)
        {
            throw new ArgumentException(
                "A Trace window requires nonempty unique ordered Probe IDs.",
                nameof(probeIds));
        }

        if (representation is TraceVisualSummaryRepresentation summary
            && (long)ownedProbeIds.Length * summary.MaxPoints > int.MaxValue)
        {
            throw new ArgumentException(
                "A Trace summary must fit a .NET collection.",
                nameof(probeIds));
        }

        ProbeIds = Array.AsReadOnly(ownedProbeIds);
        Range = range;
        Representation = representation;
        AfterSequence = afterSequence;
    }

    public ReadOnlyCollection<ProbeId> ProbeIds { get; }

    public LogicalTimeRange Range { get; }

    public TraceWindowRepresentation Representation { get; }

    public ulong? AfterSequence { get; }
}

public abstract record TraceWindowRepresentation
{
    private protected TraceWindowRepresentation()
    {
    }
}

public sealed record TraceTransitionsRepresentation : TraceWindowRepresentation
{
    private TraceTransitionsRepresentation()
    {
    }

    public static TraceTransitionsRepresentation Instance { get; } = new();
}

public sealed record TraceVisualSummaryRepresentation : TraceWindowRepresentation
{
    public const string LogicEnvelopeV1 = "logic-envelope-v1";

    public TraceVisualSummaryRepresentation(int maxPoints, string aggregation)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPoints);
        if (!string.Equals(aggregation, LogicEnvelopeV1, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The Trace summary aggregation is undefined.",
                nameof(aggregation));
        }

        MaxPoints = maxPoints;
        Aggregation = aggregation;
    }

    public int MaxPoints { get; }

    public string Aggregation { get; }
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
        ProbeSnapshot[] ownedProbes,
        TraceCursor traceCursor,
        SimulationDiagnostic[] ownedDiagnostics)
    {
        SessionId = sessionId;
        SessionVersion = sessionVersion;
        CompilationArtifactKey = compilationArtifactKey;
        LogicalTime = logicalTime;
        Probes = Array.AsReadOnly(ownedProbes);
        TraceCursor = traceCursor;
        Diagnostics = Array.AsReadOnly(ownedDiagnostics);
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
        TraceTransition[] ownedTransitions,
        LogicalTimeRange coveredRange,
        ulong earliestAvailable,
        ulong latestSequence)
    {
        Transitions = Array.AsReadOnly(ownedTransitions);
        CoveredRange = coveredRange;
        EarliestAvailable = earliestAvailable;
        LatestSequence = latestSequence;
    }

    public ReadOnlyCollection<TraceTransition> Transitions { get; }

    public LogicalTimeRange CoveredRange { get; }

    public ulong EarliestAvailable { get; }

    public ulong LatestSequence { get; }
}

public sealed record TraceSummaryBucket(
    ProbeId ProbeId,
    LogicalTimeRange Range,
    LogicVector FirstValue,
    LogicVector LastValue,
    bool HadTransition,
    bool HadMixedValues);

public sealed record TraceSummaryAvailable : SimulationReadOutcome
{
    internal TraceSummaryAvailable(
        TraceSummaryBucket[] ownedBuckets,
        string aggregation,
        LogicalTimeRange coveredRange,
        ulong earliestAvailable,
        ulong latestSequence)
    {
        ArgumentNullException.ThrowIfNull(ownedBuckets);
        ArgumentException.ThrowIfNullOrEmpty(aggregation);
        Buckets = Array.AsReadOnly(ownedBuckets);
        Aggregation = aggregation;
        CoveredRange = coveredRange;
        EarliestAvailable = earliestAvailable;
        LatestSequence = latestSequence;
    }

    public ReadOnlyCollection<TraceSummaryBucket> Buckets { get; }

    public string Aggregation { get; }

    public LogicalTimeRange CoveredRange { get; }

    public ulong EarliestAvailable { get; }

    public ulong LatestSequence { get; }
}

public sealed record TraceRangeUnavailable : SimulationReadOutcome
{
    internal TraceRangeUnavailable(
        ulong earliestAvailable,
        ulong latestSequence)
    {
        EarliestAvailable = earliestAvailable;
        LatestSequence = latestSequence;
    }

    public ulong EarliestAvailable { get; }

    public ulong LatestSequence { get; }
}

public sealed record SimulationReadFailed : SimulationReadOutcome
{
    internal SimulationReadFailed(
        SimulationFailureReason reason,
        SimulationDiagnostic[] ownedDiagnostics)
    {
        Reason = reason;
        Diagnostics = Array.AsReadOnly(ownedDiagnostics);
    }

    public SimulationFailureReason Reason { get; }

    public ReadOnlyCollection<SimulationDiagnostic> Diagnostics { get; }
}
