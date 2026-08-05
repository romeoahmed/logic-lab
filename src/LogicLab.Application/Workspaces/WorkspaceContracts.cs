using System.Collections.ObjectModel;
using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.Engine.Compilation;
using LogicLab.Engine.Simulation;

namespace LogicLab.Application.Workspaces;

public sealed record WorkspaceId
{
    public WorkspaceId(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        Value = value;
    }

    public string Value { get; }

    internal static WorkspaceId Create() => new(Guid.CreateVersion7().ToString("N"));
}

public abstract record OpenWorkspaceRequest
{
    private protected OpenWorkspaceRequest()
    {
    }
}

public sealed record CreateSandbox : OpenWorkspaceRequest
{
    public CreateSandbox(
        string projectDisplayName,
        string entryCircuitDefinitionDisplayName)
    {
        ArgumentNullException.ThrowIfNull(projectDisplayName);
        ArgumentNullException.ThrowIfNull(entryCircuitDefinitionDisplayName);
        ProjectDisplayName = projectDisplayName;
        EntryCircuitDefinitionDisplayName = entryCircuitDefinitionDisplayName;
    }

    public string ProjectDisplayName { get; }

    public string EntryCircuitDefinitionDisplayName { get; }
}

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

public abstract record WorkspaceCommand
{
    private protected WorkspaceCommand(WorkspaceCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Context = context;
    }

    public WorkspaceId WorkspaceId => Context.WorkspaceId;

    public WorkspaceCommandContext Context { get; }
}

public sealed record ApplyEdit : WorkspaceCommand
{
    public ApplyEdit(
        WorkspaceCommandContext context,
        AuthoringPrecondition precondition,
        EditIntent intent)
        : base(context)
    {
        ArgumentNullException.ThrowIfNull(precondition);
        ArgumentNullException.ThrowIfNull(intent);
        Precondition = precondition;
        Intent = intent;
    }

    public EditIntent Intent { get; }

    public AuthoringPrecondition Precondition { get; }
}

public sealed record RequestCompilation : WorkspaceCommand
{
    public RequestCompilation(
        WorkspaceCommandContext context,
        CompilationPrecondition precondition)
        : base(context)
    {
        ArgumentNullException.ThrowIfNull(precondition);
        Precondition = precondition;
    }

    public CompilationPrecondition Precondition { get; }
}

public sealed record CreateSession : WorkspaceCommand
{
    public CreateSession(
        WorkspaceCommandContext context,
        SessionCreationPrecondition precondition)
        : base(context)
    {
        ArgumentNullException.ThrowIfNull(precondition);
        Precondition = precondition;
    }

    public SessionCreationPrecondition Precondition { get; }
}

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
        WorkspaceCommandContext context,
        SessionMutationPrecondition precondition,
        ulong logicalTime,
        IReadOnlyList<InputStimulusAssignment> assignments)
        : base(context)
    {
        ArgumentNullException.ThrowIfNull(precondition);
        Precondition = precondition;
        LogicalTime = logicalTime;
        Assignments = CopyAssignments(assignments);
    }

    public ulong LogicalTime { get; }

    public ReadOnlyCollection<InputStimulusAssignment> Assignments { get; }

    public SessionMutationPrecondition Precondition { get; }

    private static ReadOnlyCollection<InputStimulusAssignment> CopyAssignments(
        IReadOnlyList<InputStimulusAssignment> assignments)
    {
        ArgumentNullException.ThrowIfNull(assignments);
        var copy = assignments.ToArray();
        if (copy.Any(static assignment => assignment is null))
        {
            throw new ArgumentException(
                "The collection must not contain null elements.",
                nameof(assignments));
        }

        return Array.AsReadOnly(copy);
    }
}

public sealed record StepSession : WorkspaceCommand
{
    public StepSession(
        WorkspaceCommandContext context,
        SessionMutationPrecondition precondition)
        : base(context)
    {
        ArgumentNullException.ThrowIfNull(precondition);
        Precondition = precondition;
    }

    public SessionMutationPrecondition Precondition { get; }
}

public sealed record CloseWorkspace : WorkspaceCommand
{
    public CloseWorkspace(WorkspaceCommandContext context)
        : base(context)
    {
    }
}

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

public sealed record WorkspaceClosed(WorkspaceId WorkspaceId) : WorkspaceCommandOutcome;

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
        TraceCursor traceCursor,
        IReadOnlyList<ProbeProjection> probes)
    {
        ArgumentNullException.ThrowIfNull(sessionId);
        ArgumentNullException.ThrowIfNull(probes);
        SessionId = sessionId;
        SessionVersion = sessionVersion;
        LogicalTime = logicalTime;
        TraceCursor = traceCursor;
        Probes = Array.AsReadOnly(probes.ToArray());
    }

    public SimulationSessionId SessionId { get; }

    public ulong SessionVersion { get; }

    public ulong LogicalTime { get; }

    public TraceCursor TraceCursor { get; }

    public ReadOnlyCollection<ProbeProjection> Probes { get; }
}

public sealed record WorkspaceProjection(
    WorkspaceId WorkspaceId,
    ulong ProjectionVersion,
    ProjectRevision ProjectRevision,
    CompilationProjection Compilation,
    SimulationProjection? Simulation)
{
    public TransactionHistoryAvailability History { get; init; } = new(
        CanUndo: false,
        CanRedo: false,
        RetainedRevisionCount: 1);
}
