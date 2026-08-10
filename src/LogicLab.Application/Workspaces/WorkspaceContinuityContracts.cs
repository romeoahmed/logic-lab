using System.Collections.ObjectModel;
using LogicLab.Domain.Authoring;
using LogicLab.Engine.Compilation;
using LogicLab.Engine.Simulation;

namespace LogicLab.Application.Workspaces;

internal static class WorkspaceBuild
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
        ClientIntentId clientIntentId,
        WorkspaceCaller caller)
    {
        ArgumentNullException.ThrowIfNull(workspaceId);
        ArgumentNullException.ThrowIfNull(attachmentId);
        ArgumentOutOfRangeException.ThrowIfZero(attachmentGeneration);
        ArgumentNullException.ThrowIfNull(clientIntentId);
        ArgumentNullException.ThrowIfNull(caller);
        WorkspaceId = workspaceId;
        AttachmentId = attachmentId;
        AttachmentGeneration = attachmentGeneration;
        ClientIntentId = clientIntentId;
        Caller = caller;
    }

    public WorkspaceId WorkspaceId { get; }

    public WorkspaceAttachmentId AttachmentId { get; }

    public ulong AttachmentGeneration { get; }

    public ClientIntentId ClientIntentId { get; }

    public WorkspaceCaller Caller { get; }
}

public sealed record WorkspaceQueryContext
{
    public WorkspaceQueryContext(
        WorkspaceId workspaceId,
        WorkspaceAttachmentId attachmentId,
        ulong attachmentGeneration,
        WorkspaceCaller caller)
    {
        ArgumentNullException.ThrowIfNull(workspaceId);
        ArgumentNullException.ThrowIfNull(attachmentId);
        ArgumentOutOfRangeException.ThrowIfZero(attachmentGeneration);
        ArgumentNullException.ThrowIfNull(caller);
        WorkspaceId = workspaceId;
        AttachmentId = attachmentId;
        AttachmentGeneration = attachmentGeneration;
        Caller = caller;
    }

    public WorkspaceId WorkspaceId { get; }

    public WorkspaceAttachmentId AttachmentId { get; }

    public ulong AttachmentGeneration { get; }

    public WorkspaceCaller Caller { get; }
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

public sealed record RunGeneration
{
    public RunGeneration(ulong value)
    {
        ArgumentOutOfRangeException.ThrowIfZero(value);
        Value = value;
    }

    public ulong Value { get; }
}

public sealed record RunControlPrecondition
{
    public RunControlPrecondition(
        SimulationSessionId sessionId,
        RunGeneration runGeneration)
    {
        ArgumentNullException.ThrowIfNull(sessionId);
        ArgumentNullException.ThrowIfNull(runGeneration);
        SessionId = sessionId;
        RunGeneration = runGeneration;
    }

    public SimulationSessionId SessionId { get; }

    public RunGeneration RunGeneration { get; }
}

public sealed record TransactionHistoryAvailability(
    bool CanUndo,
    bool CanRedo,
    int RetainedRevisionCount);

public abstract record AttachRequest
{
    private protected AttachRequest(
        WorkspaceId workspaceId,
        string buildFingerprint,
        WorkspaceCaller caller)
    {
        ArgumentNullException.ThrowIfNull(workspaceId);
        ArgumentException.ThrowIfNullOrEmpty(buildFingerprint);
        ArgumentNullException.ThrowIfNull(caller);
        WorkspaceId = workspaceId;
        BuildFingerprint = buildFingerprint;
        Caller = caller;
    }

    public WorkspaceId WorkspaceId { get; }

    public string BuildFingerprint { get; }

    public WorkspaceCaller Caller { get; }
}

public sealed record InitialAttach : AttachRequest
{
    public InitialAttach(
        WorkspaceId workspaceId,
        string buildFingerprint,
        WorkspaceCaller caller)
        : base(workspaceId, buildFingerprint, caller)
    {
    }
}

public sealed record Reattach : AttachRequest
{
    public Reattach(
        WorkspaceId workspaceId,
        WorkspaceAttachmentId priorAttachmentId,
        ulong priorGeneration,
        string buildFingerprint,
        WorkspaceCaller caller)
        : base(workspaceId, buildFingerprint, caller)
    {
        ArgumentNullException.ThrowIfNull(priorAttachmentId);
        ArgumentOutOfRangeException.ThrowIfZero(priorGeneration);
        PriorAttachmentId = priorAttachmentId;
        PriorGeneration = priorGeneration;
    }

    public WorkspaceAttachmentId PriorAttachmentId { get; }

    public ulong PriorGeneration { get; }
}

public sealed record RecoverAttach : AttachRequest
{
    public RecoverAttach(
        WorkspaceId workspaceId,
        string buildFingerprint,
        WorkspaceCaller caller)
        : base(workspaceId, buildFingerprint, caller)
    {
    }
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
    public AttachRejected(
        string code,
        IReadOnlyList<string> diagnosticCodes,
        RetryDisposition retryDisposition)
    {
        ArgumentException.ThrowIfNullOrEmpty(code);
        ArgumentNullException.ThrowIfNull(diagnosticCodes);
        Code = code;
        DiagnosticCodes = Array.AsReadOnly(diagnosticCodes.ToArray());
        RetryDisposition = retryDisposition;
    }

    public string Code { get; }

    public ReadOnlyCollection<string> DiagnosticCodes { get; }

    public RetryDisposition RetryDisposition { get; }
}

public sealed record DetachRequest
{
    public DetachRequest(
        WorkspaceId workspaceId,
        WorkspaceAttachmentId attachmentId,
        ulong attachmentGeneration,
        WorkspaceCaller caller)
    {
        ArgumentNullException.ThrowIfNull(workspaceId);
        ArgumentNullException.ThrowIfNull(attachmentId);
        ArgumentOutOfRangeException.ThrowIfZero(attachmentGeneration);
        ArgumentNullException.ThrowIfNull(caller);
        WorkspaceId = workspaceId;
        AttachmentId = attachmentId;
        AttachmentGeneration = attachmentGeneration;
        Caller = caller;
    }

    public WorkspaceId WorkspaceId { get; }

    public WorkspaceAttachmentId AttachmentId { get; }

    public ulong AttachmentGeneration { get; }

    public WorkspaceCaller Caller { get; }
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
        WorkspaceCopySaveTarget saveTarget,
        WorkspaceCaller caller)
    {
        ArgumentNullException.ThrowIfNull(sourceWorkspaceId);
        ArgumentNullException.ThrowIfNull(sourceAttachmentId);
        ArgumentOutOfRangeException.ThrowIfZero(sourceAttachmentGeneration);
        ArgumentNullException.ThrowIfNull(caller);
        if (!Enum.IsDefined(saveTarget))
        {
            throw new ArgumentOutOfRangeException(nameof(saveTarget));
        }

        SourceWorkspaceId = sourceWorkspaceId;
        SourceAttachmentId = sourceAttachmentId;
        SourceAttachmentGeneration = sourceAttachmentGeneration;
        ExpectedProjectionVersion = expectedProjectionVersion;
        SaveTarget = saveTarget;
        Caller = caller;
    }

    public WorkspaceId SourceWorkspaceId { get; }

    public WorkspaceAttachmentId SourceAttachmentId { get; }

    public ulong SourceAttachmentGeneration { get; }

    public ulong ExpectedProjectionVersion { get; }

    public WorkspaceCopySaveTarget SaveTarget { get; }

    public WorkspaceCaller Caller { get; }
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
