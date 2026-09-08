using System.Collections.ObjectModel;
using LogicLab.Domain;
using LogicLab.Engine.Compilation;
using LogicLab.Engine.Simulation;

namespace LogicLab.Application.Workspaces;

public enum AdvanceFailureReason
{
    ZeroTimeOscillation,
    SimulationResourceLimit,
    SimulationCancelled,
    SimulationInfrastructureFailure,
    SimulationInternalDefect,
}

public sealed record AdvanceFailureProjection
{
    public AdvanceFailureProjection(
        AdvanceFailureReason reason,
        IReadOnlyList<string> diagnosticCodes,
        PolicyEvidenceProjection? policyEvidence)
    {
        ArgumentNullException.ThrowIfNull(diagnosticCodes);
        if (!Enum.IsDefined(reason)
            || (reason == AdvanceFailureReason.SimulationResourceLimit)
                != (policyEvidence is not null))
        {
            throw new ArgumentException(
                "Advance failure fields do not match its reason.");
        }

        Reason = reason;
        DiagnosticCodes = Array.AsReadOnly(diagnosticCodes.ToArray());
        PolicyEvidence = policyEvidence;
    }

    public AdvanceFailureReason Reason { get; }

    public ReadOnlyCollection<string> DiagnosticCodes { get; }

    public PolicyEvidenceProjection? PolicyEvidence { get; }
}

public enum RunPauseReason
{
    UserRequested,
    NoScheduledStimulus,
    Detached,
}

public sealed class HotSwapMigrationProjection
{
    public HotSwapMigrationProjection(
        IReadOnlyList<CompilationSource> migratedStateSources,
        IReadOnlyList<ProbeId> preservedProbeIds,
        IReadOnlyList<ProbeId> unresolvedProbeIds)
    {
        ArgumentNullException.ThrowIfNull(migratedStateSources);
        ArgumentNullException.ThrowIfNull(preservedProbeIds);
        ArgumentNullException.ThrowIfNull(unresolvedProbeIds);
        MigratedStateSources = Array.AsReadOnly(migratedStateSources.ToArray());
        PreservedProbeIds = Array.AsReadOnly(preservedProbeIds.ToArray());
        UnresolvedProbeIds = Array.AsReadOnly(unresolvedProbeIds.ToArray());
    }

    private HotSwapMigrationProjection(
        ReadOnlyCollection<CompilationSource> migratedStateSources,
        ReadOnlyCollection<ProbeId> preservedProbeIds,
        ReadOnlyCollection<ProbeId> unresolvedProbeIds)
    {
        MigratedStateSources = migratedStateSources;
        PreservedProbeIds = preservedProbeIds;
        UnresolvedProbeIds = unresolvedProbeIds;
    }

    public ReadOnlyCollection<CompilationSource> MigratedStateSources { get; }

    public ReadOnlyCollection<ProbeId> PreservedProbeIds { get; }

    public ReadOnlyCollection<ProbeId> UnresolvedProbeIds { get; }

    internal static HotSwapMigrationProjection FromImmutable(
        HotSwapMigrationEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        return new HotSwapMigrationProjection(
            evidence.MigratedStateSources,
            evidence.PreservedProbeIds,
            evidence.UnresolvedProbeIds);
    }
}

public sealed record ProbeProjection
{
    public ProbeProjection(
        ProbeId probeId,
        CompilationSource source,
        IReadOnlyList<LogicValue> value)
    {
        ProbeId = probeId;
        Source = source;
        Value = Array.AsReadOnly(value.ToArray());
    }

    private ProbeProjection(
        ProbeId probeId,
        CompilationSource source,
        ReadOnlyCollection<LogicValue> value)
    {
        ProbeId = probeId;
        Source = source;
        Value = value;
    }

    public ProbeId ProbeId { get; }

    public CompilationSource Source { get; }

    public ReadOnlyCollection<LogicValue> Value { get; }

    internal static ProbeProjection FromOwnedValue(
        ProbeId probeId,
        CompilationSource source,
        LogicValue[] ownedValue)
    {
        ArgumentNullException.ThrowIfNull(probeId);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(ownedValue);
        return new ProbeProjection(
            probeId,
            source,
            Array.AsReadOnly(ownedValue));
    }
}

public enum RunStatus
{
    NotRunning,
    Running,
    Paused,
    Failed,
}

public abstract record RunProjection
{
    private protected RunProjection(RunStatus status)
    {
        Status = status;
    }

    public RunStatus Status { get; }

    public virtual RunGeneration? RunGeneration => null;
}

public sealed record RunNotRunningProjection : RunProjection
{
    private RunNotRunningProjection()
        : base(RunStatus.NotRunning)
    {
    }

    public static RunNotRunningProjection Instance { get; } = new();
}

public sealed record RunRunningProjection : RunProjection
{
    public RunRunningProjection(RunGeneration runGeneration)
        : base(RunStatus.Running)
    {
        ArgumentNullException.ThrowIfNull(runGeneration);
        RunGeneration = runGeneration;
    }

    public override RunGeneration RunGeneration { get; }
}

public sealed record RunPausedProjection : RunProjection
{
    public RunPausedProjection(
        RunGeneration runGeneration,
        RunPauseReason pauseReason)
        : base(RunStatus.Paused)
    {
        ArgumentNullException.ThrowIfNull(runGeneration);
        if (!Enum.IsDefined(pauseReason))
        {
            throw new ArgumentOutOfRangeException(nameof(pauseReason));
        }

        RunGeneration = runGeneration;
        PauseReason = pauseReason;
    }

    public override RunGeneration RunGeneration { get; }

    public RunPauseReason PauseReason { get; }
}

public sealed record RunFailedProjection : RunProjection
{
    public RunFailedProjection(
        RunGeneration runGeneration,
        AdvanceFailureProjection failure)
        : base(RunStatus.Failed)
    {
        ArgumentNullException.ThrowIfNull(runGeneration);
        ArgumentNullException.ThrowIfNull(failure);
        RunGeneration = runGeneration;
        Failure = failure;
    }

    public override RunGeneration RunGeneration { get; }

    public AdvanceFailureProjection Failure { get; }
}

public sealed record SimulationProjection
{
    public SimulationProjection(
        SimulationSessionId sessionId,
        ulong sessionVersion,
        CompilationArtifactKey compilationArtifactKey,
        ulong logicalTime,
        TraceCursor traceCursor,
        IReadOnlyList<ProbeProjection> probes,
        RunProjection run,
        IReadOnlyList<SimulationDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(sessionId);
        ArgumentNullException.ThrowIfNull(compilationArtifactKey);
        ArgumentNullException.ThrowIfNull(probes);
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(diagnostics);
        SessionId = sessionId;
        SessionVersion = sessionVersion;
        CompilationArtifactKey = compilationArtifactKey;
        LogicalTime = logicalTime;
        TraceCursor = traceCursor;
        Probes = Array.AsReadOnly(probes.ToArray());
        Run = run;
        Diagnostics = Array.AsReadOnly(diagnostics.ToArray());
    }

    private SimulationProjection(
        SimulationSessionId sessionId,
        ulong sessionVersion,
        CompilationArtifactKey compilationArtifactKey,
        ulong logicalTime,
        TraceCursor traceCursor,
        ReadOnlyCollection<ProbeProjection> probes,
        RunProjection run,
        ReadOnlyCollection<SimulationDiagnostic> diagnostics)
    {
        SessionId = sessionId;
        SessionVersion = sessionVersion;
        CompilationArtifactKey = compilationArtifactKey;
        LogicalTime = logicalTime;
        TraceCursor = traceCursor;
        Probes = probes;
        Run = run;
        Diagnostics = diagnostics;
    }

    public SimulationSessionId SessionId { get; }

    public ulong SessionVersion { get; private init; }

    public CompilationArtifactKey CompilationArtifactKey { get; }

    public ulong LogicalTime { get; }

    public TraceCursor TraceCursor { get; }

    public ReadOnlyCollection<ProbeProjection> Probes { get; }

    public RunProjection Run { get; private init; }

    public ReadOnlyCollection<SimulationDiagnostic> Diagnostics { get; }

    internal SimulationProjection WithSessionVersion(ulong sessionVersion) =>
        this with { SessionVersion = sessionVersion };

    internal SimulationProjection WithRun(RunProjection run) =>
        this with { Run = run };

    internal static SimulationProjection FromOwnedProbes(
        SimulationSessionId sessionId,
        ulong sessionVersion,
        CompilationArtifactKey compilationArtifactKey,
        ulong logicalTime,
        TraceCursor traceCursor,
        ProbeProjection[] ownedProbes,
        RunProjection run,
        ReadOnlyCollection<SimulationDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(sessionId);
        ArgumentNullException.ThrowIfNull(compilationArtifactKey);
        ArgumentNullException.ThrowIfNull(ownedProbes);
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(diagnostics);
        return new SimulationProjection(
            sessionId,
            sessionVersion,
            compilationArtifactKey,
            logicalTime,
            traceCursor,
            Array.AsReadOnly(ownedProbes),
            run,
            diagnostics);
    }
}
