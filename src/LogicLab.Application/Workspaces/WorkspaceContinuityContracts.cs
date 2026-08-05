using LogicLab.Domain.Authoring;
using LogicLab.Engine.Compilation;
using LogicLab.Engine.Simulation;

namespace LogicLab.Application.Workspaces;

public static class WorkspaceBuild
{
    public const string DevelopmentFingerprint = "logiclab-development";
}

public sealed record WorkspaceAttachmentId
{
    public WorkspaceAttachmentId(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        Value = value;
    }

    public string Value { get; }

    internal static WorkspaceAttachmentId Create()
        => new(Guid.CreateVersion7().ToString("N"));
}

public sealed record ClientIntentId
{
    public ClientIntentId(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        Value = value;
    }

    public string Value { get; }
}

public sealed record WorkspaceCommandContext
{
    public WorkspaceCommandContext(
        WorkspaceId workspaceId,
        WorkspaceAttachmentId attachmentId,
        ulong attachmentGeneration,
        ClientIntentId clientIntentId)
    {
        ArgumentNullException.ThrowIfNull(workspaceId);
        ArgumentNullException.ThrowIfNull(attachmentId);
        ArgumentOutOfRangeException.ThrowIfZero(attachmentGeneration);
        ArgumentNullException.ThrowIfNull(clientIntentId);
        WorkspaceId = workspaceId;
        AttachmentId = attachmentId;
        AttachmentGeneration = attachmentGeneration;
        ClientIntentId = clientIntentId;
    }

    public WorkspaceId WorkspaceId { get; }

    public WorkspaceAttachmentId AttachmentId { get; }

    public ulong AttachmentGeneration { get; }

    public ClientIntentId ClientIntentId { get; }
}

public sealed record WorkspaceQueryContext
{
    public WorkspaceQueryContext(
        WorkspaceId workspaceId,
        WorkspaceAttachmentId attachmentId,
        ulong attachmentGeneration)
    {
        ArgumentNullException.ThrowIfNull(workspaceId);
        ArgumentNullException.ThrowIfNull(attachmentId);
        ArgumentOutOfRangeException.ThrowIfZero(attachmentGeneration);
        WorkspaceId = workspaceId;
        AttachmentId = attachmentId;
        AttachmentGeneration = attachmentGeneration;
    }

    public WorkspaceId WorkspaceId { get; }

    public WorkspaceAttachmentId AttachmentId { get; }

    public ulong AttachmentGeneration { get; }
}

public sealed record AuthoringPrecondition
{
    public AuthoringPrecondition(ProjectRevisionId projectRevisionId)
    {
        ArgumentNullException.ThrowIfNull(projectRevisionId);
        ProjectRevisionId = projectRevisionId;
    }

    public ProjectRevisionId ProjectRevisionId { get; }
}

public sealed record CompilationPrecondition
{
    public CompilationPrecondition(
        ProjectRevisionId projectRevisionId,
        CircuitDefinitionId entryCircuitDefinitionId,
        string librarySnapshotFingerprint)
    {
        ArgumentNullException.ThrowIfNull(projectRevisionId);
        ArgumentNullException.ThrowIfNull(entryCircuitDefinitionId);
        ArgumentException.ThrowIfNullOrEmpty(librarySnapshotFingerprint);
        ProjectRevisionId = projectRevisionId;
        EntryCircuitDefinitionId = entryCircuitDefinitionId;
        LibrarySnapshotFingerprint = librarySnapshotFingerprint;
    }

    public ProjectRevisionId ProjectRevisionId { get; }

    public CircuitDefinitionId EntryCircuitDefinitionId { get; }

    public string LibrarySnapshotFingerprint { get; }
}

public sealed record SessionCreationPrecondition
{
    public SessionCreationPrecondition(CompilationArtifactKey compilationArtifactKey)
    {
        ArgumentNullException.ThrowIfNull(compilationArtifactKey);
        CompilationArtifactKey = compilationArtifactKey;
    }

    public CompilationArtifactKey CompilationArtifactKey { get; }
}

public sealed record SessionMutationPrecondition
{
    public SessionMutationPrecondition(
        SimulationSessionId sessionId,
        ulong sessionVersion,
        CompilationArtifactKey compilationArtifactKey)
    {
        ArgumentNullException.ThrowIfNull(sessionId);
        ArgumentNullException.ThrowIfNull(compilationArtifactKey);
        SessionId = sessionId;
        SessionVersion = sessionVersion;
        CompilationArtifactKey = compilationArtifactKey;
    }

    public SimulationSessionId SessionId { get; }

    public ulong SessionVersion { get; }

    public CompilationArtifactKey CompilationArtifactKey { get; }
}

public sealed record TransactionHistoryAvailability(
    bool CanUndo,
    bool CanRedo,
    int RetainedRevisionCount);

public abstract record AttachRequest
{
    private protected AttachRequest(WorkspaceId workspaceId, string buildFingerprint)
    {
        ArgumentNullException.ThrowIfNull(workspaceId);
        ArgumentException.ThrowIfNullOrEmpty(buildFingerprint);
        WorkspaceId = workspaceId;
        BuildFingerprint = buildFingerprint;
    }

    public WorkspaceId WorkspaceId { get; }

    public string BuildFingerprint { get; }
}

public sealed record InitialAttach : AttachRequest
{
    public InitialAttach(WorkspaceId workspaceId, string buildFingerprint)
        : base(workspaceId, buildFingerprint)
    {
    }
}

public sealed record Reattach : AttachRequest
{
    public Reattach(
        WorkspaceId workspaceId,
        WorkspaceAttachmentId priorAttachmentId,
        ulong priorGeneration,
        ulong lastProjectionVersion,
        string buildFingerprint)
        : base(workspaceId, buildFingerprint)
    {
        ArgumentNullException.ThrowIfNull(priorAttachmentId);
        ArgumentOutOfRangeException.ThrowIfZero(priorGeneration);
        PriorAttachmentId = priorAttachmentId;
        PriorGeneration = priorGeneration;
        LastProjectionVersion = lastProjectionVersion;
    }

    public WorkspaceAttachmentId PriorAttachmentId { get; }

    public ulong PriorGeneration { get; }

    public ulong LastProjectionVersion { get; }
}

public abstract record WorkspaceAttachOutcome
{
    private protected WorkspaceAttachOutcome()
    {
    }
}

public sealed record Attached(
    WorkspaceAttachmentId AttachmentId,
    ulong Generation,
    WorkspaceProjection Projection) : WorkspaceAttachOutcome;

public sealed record Expired(string Code) : WorkspaceAttachOutcome;

public sealed record AttachRejected : WorkspaceAttachOutcome
{
    public AttachRejected(string code)
    {
        ArgumentException.ThrowIfNullOrEmpty(code);
        Code = code;
    }

    public string Code { get; }
}

public sealed record DetachRequest
{
    public DetachRequest(
        WorkspaceId workspaceId,
        WorkspaceAttachmentId attachmentId,
        ulong attachmentGeneration)
    {
        ArgumentNullException.ThrowIfNull(workspaceId);
        ArgumentNullException.ThrowIfNull(attachmentId);
        ArgumentOutOfRangeException.ThrowIfZero(attachmentGeneration);
        WorkspaceId = workspaceId;
        AttachmentId = attachmentId;
        AttachmentGeneration = attachmentGeneration;
    }

    public WorkspaceId WorkspaceId { get; }

    public WorkspaceAttachmentId AttachmentId { get; }

    public ulong AttachmentGeneration { get; }
}

public abstract record WorkspaceDetachOutcome
{
    private protected WorkspaceDetachOutcome()
    {
    }
}

public sealed record Detached(
    WorkspaceId WorkspaceId,
    ulong AttachmentGeneration) : WorkspaceDetachOutcome;

public sealed record DetachRejected : WorkspaceDetachOutcome
{
    public DetachRejected(string code)
    {
        ArgumentException.ThrowIfNullOrEmpty(code);
        Code = code;
    }

    public string Code { get; }
}

public enum WorkspaceCopySaveTarget
{
    Preserve,
    DetachedSandbox,
}

public sealed record CopyWorkspace : OpenWorkspaceRequest
{
    public CopyWorkspace(
        WorkspaceId sourceWorkspaceId,
        WorkspaceAttachmentId sourceAttachmentId,
        ulong sourceAttachmentGeneration,
        ulong expectedProjectionVersion,
        WorkspaceCopySaveTarget saveTarget)
    {
        ArgumentNullException.ThrowIfNull(sourceWorkspaceId);
        ArgumentNullException.ThrowIfNull(sourceAttachmentId);
        ArgumentOutOfRangeException.ThrowIfZero(sourceAttachmentGeneration);
        if (!Enum.IsDefined(saveTarget))
        {
            throw new ArgumentOutOfRangeException(nameof(saveTarget));
        }

        SourceWorkspaceId = sourceWorkspaceId;
        SourceAttachmentId = sourceAttachmentId;
        SourceAttachmentGeneration = sourceAttachmentGeneration;
        ExpectedProjectionVersion = expectedProjectionVersion;
        SaveTarget = saveTarget;
    }

    public WorkspaceId SourceWorkspaceId { get; }

    public WorkspaceAttachmentId SourceAttachmentId { get; }

    public ulong SourceAttachmentGeneration { get; }

    public ulong ExpectedProjectionVersion { get; }

    public WorkspaceCopySaveTarget SaveTarget { get; }
}

public sealed record Undo : WorkspaceCommand
{
    public Undo(WorkspaceCommandContext context, AuthoringPrecondition precondition)
        : base(context)
    {
        ArgumentNullException.ThrowIfNull(precondition);
        Precondition = precondition;
    }

    public AuthoringPrecondition Precondition { get; }
}

public sealed record Redo : WorkspaceCommand
{
    public Redo(WorkspaceCommandContext context, AuthoringPrecondition precondition)
        : base(context)
    {
        ArgumentNullException.ThrowIfNull(precondition);
        Precondition = precondition;
    }

    public AuthoringPrecondition Precondition { get; }
}
