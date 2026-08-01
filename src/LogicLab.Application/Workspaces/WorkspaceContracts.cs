using System.Collections.ObjectModel;
using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.Engine.Compilation;
using LogicLab.Engine.Simulation;

namespace LogicLab.Application.Workspaces;

public sealed record WorkspaceId(string Value)
{
    internal static WorkspaceId Create() => new(Guid.CreateVersion7().ToString("N"));
}

public abstract record OpenWorkspaceRequest
{
    private protected OpenWorkspaceRequest()
    {
    }
}

public sealed record CreateSandbox(
    string ProjectDisplayName,
    string EntryCircuitDefinitionDisplayName) : OpenWorkspaceRequest;

public abstract record WorkspaceOpenOutcome
{
    private protected WorkspaceOpenOutcome()
    {
    }
}

public sealed record WorkspaceOpened(
    WorkspaceId WorkspaceId,
    WorkspaceProjection Projection) : WorkspaceOpenOutcome;

public sealed record WorkspaceOpenRejected : WorkspaceOpenOutcome
{
    public WorkspaceOpenRejected(string code, IReadOnlyList<string> diagnosticCodes)
    {
        ArgumentException.ThrowIfNullOrEmpty(code);
        ArgumentNullException.ThrowIfNull(diagnosticCodes);
        Code = code;
        DiagnosticCodes = Array.AsReadOnly(diagnosticCodes.ToArray());
    }

    public string Code { get; }

    public ReadOnlyCollection<string> DiagnosticCodes { get; }
}

public abstract record WorkspaceCommand(WorkspaceId WorkspaceId);

public sealed record ApplyEdit(WorkspaceId WorkspaceId, EditIntent Intent)
    : WorkspaceCommand(WorkspaceId);

public sealed record RequestCompilation(WorkspaceId WorkspaceId)
    : WorkspaceCommand(WorkspaceId);

public sealed record CreateSession(WorkspaceId WorkspaceId)
    : WorkspaceCommand(WorkspaceId);

public sealed record InputStimulusAssignment
{
    public InputStimulusAssignment(
        ComponentInstanceId inputComponentInstanceId,
        IReadOnlyList<LogicValue> value)
    {
        ArgumentNullException.ThrowIfNull(inputComponentInstanceId);
        ArgumentNullException.ThrowIfNull(value);
        InputComponentInstanceId = inputComponentInstanceId;
        Value = Array.AsReadOnly(value.ToArray());
    }

    public ComponentInstanceId InputComponentInstanceId { get; }

    public ReadOnlyCollection<LogicValue> Value { get; }
}

public sealed record ScheduleInputStimulus : WorkspaceCommand
{
    public ScheduleInputStimulus(
        WorkspaceId workspaceId,
        ulong logicalTime,
        IReadOnlyList<InputStimulusAssignment> assignments)
        : base(workspaceId)
    {
        ArgumentNullException.ThrowIfNull(assignments);
        LogicalTime = logicalTime;
        Assignments = Array.AsReadOnly(assignments.ToArray());
    }

    public ulong LogicalTime { get; }

    public ReadOnlyCollection<InputStimulusAssignment> Assignments { get; }
}

public sealed record StepSession(WorkspaceId WorkspaceId)
    : WorkspaceCommand(WorkspaceId);

public abstract record WorkspaceCommandOutcome
{
    private protected WorkspaceCommandOutcome()
    {
    }
}

public sealed record AuthoringCommitted(
    ProjectRevisionId ProjectRevisionId,
    ulong ProjectionVersion) : WorkspaceCommandOutcome;

public sealed record CompilationPublished(
    CompilationArtifactKey ArtifactKey,
    ulong ProjectionVersion) : WorkspaceCommandOutcome;

public sealed record SimulationSessionCreated(
    SimulationSessionId SessionId,
    ulong ProjectionVersion) : WorkspaceCommandOutcome;

public sealed record StimulusScheduled(
    ulong LogicalTime,
    ulong ProjectionVersion) : WorkspaceCommandOutcome;

public sealed record SessionStepped(
    ulong LogicalTime,
    ulong ProjectionVersion) : WorkspaceCommandOutcome;

public sealed record WorkspaceCommandRejected : WorkspaceCommandOutcome
{
    public WorkspaceCommandRejected(string code, IReadOnlyList<string> diagnosticCodes)
    {
        ArgumentException.ThrowIfNullOrEmpty(code);
        ArgumentNullException.ThrowIfNull(diagnosticCodes);
        Code = code;
        DiagnosticCodes = Array.AsReadOnly(diagnosticCodes.ToArray());
    }

    public string Code { get; }

    public ReadOnlyCollection<string> DiagnosticCodes { get; }
}

public abstract record WorkspaceReadOutcome
{
    private protected WorkspaceReadOutcome()
    {
    }
}

public sealed record ProjectionSnapshot(WorkspaceProjection Projection)
    : WorkspaceReadOutcome;

public sealed record WorkspaceReadRejected(string Code) : WorkspaceReadOutcome;

public enum CompilationPublicationStatus
{
    NotRequested,
    Published,
    Rejected,
}

public sealed record CompilationProjection
{
    public CompilationProjection(
        CompilationPublicationStatus status,
        CompilationArtifactKey? artifactKey,
        IReadOnlyList<string> diagnosticCodes)
    {
        ArgumentNullException.ThrowIfNull(diagnosticCodes);
        Status = status;
        ArtifactKey = artifactKey;
        DiagnosticCodes = Array.AsReadOnly(diagnosticCodes.ToArray());
    }

    public CompilationPublicationStatus Status { get; }

    public CompilationArtifactKey? ArtifactKey { get; }

    public ReadOnlyCollection<string> DiagnosticCodes { get; }
}

public sealed record ProbeProjection
{
    public ProbeProjection(
        ProbeId probeId,
        AuthoredSourceIdentity source,
        IReadOnlyList<LogicValue> value)
    {
        ProbeId = probeId;
        Source = source;
        Value = Array.AsReadOnly(value.ToArray());
    }

    public ProbeId ProbeId { get; }

    public AuthoredSourceIdentity Source { get; }

    public ReadOnlyCollection<LogicValue> Value { get; }
}

public sealed record SimulationProjection
{
    public SimulationProjection(
        SimulationSessionId sessionId,
        ulong sessionVersion,
        ulong logicalTime,
        IReadOnlyList<ProbeProjection> probes)
    {
        ArgumentNullException.ThrowIfNull(sessionId);
        ArgumentNullException.ThrowIfNull(probes);
        SessionId = sessionId;
        SessionVersion = sessionVersion;
        LogicalTime = logicalTime;
        Probes = Array.AsReadOnly(probes.ToArray());
    }

    public SimulationSessionId SessionId { get; }

    public ulong SessionVersion { get; }

    public ulong LogicalTime { get; }

    public ReadOnlyCollection<ProbeProjection> Probes { get; }
}

public sealed record WorkspaceProjection(
    WorkspaceId WorkspaceId,
    ulong ProjectionVersion,
    ProjectRevision ProjectRevision,
    CompilationProjection Compilation,
    SimulationProjection? Simulation);
