using System.Text;
using LogicLab.Domain.Authoring;

namespace LogicLab.Application.Workspaces;

public sealed record AuthenticatedSubjectId
{
    public AuthenticatedSubjectId(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        Value = value;
    }

    public string Value { get; }
}

public abstract record WorkspaceCaller
{
    private protected WorkspaceCaller()
    {
    }
}

public sealed record AnonymousWorkspaceCaller : WorkspaceCaller
{
    private AnonymousWorkspaceCaller()
    {
    }

    public static AnonymousWorkspaceCaller Instance { get; } = new();
}

public sealed record AuthenticatedWorkspaceCaller : WorkspaceCaller
{
    public AuthenticatedWorkspaceCaller(AuthenticatedSubjectId subjectId)
    {
        ArgumentNullException.ThrowIfNull(subjectId);
        SubjectId = subjectId;
    }

    public AuthenticatedSubjectId SubjectId { get; }
}

public sealed record DurableProjectId
{
    public DurableProjectId(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        Value = value;
    }

    public string Value { get; }

    internal static DurableProjectId Create()
        => new(Guid.CreateVersion7().ToString("N"));
}

public sealed record DurableVersion
{
    public DurableVersion(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        Value = value;
    }

    public string Value { get; }

    internal static DurableVersion Create()
        => new(Guid.CreateVersion7().ToString("N"));
}

public sealed record DurableDisplayName
{
    public DurableDisplayName(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        if (!IsValid(value))
        {
            throw new ArgumentException(
                "A Durable Display Name must be NFC Unicode without C0 controls or isolated surrogates.",
                nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    internal static bool IsValid(string value)
        => value.Length > 0
            && HasValidScalars(value)
            && value.IsNormalized(NormalizationForm.FormC);

    private static bool HasValidScalars(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (character <= '\u001f' || char.IsLowSurrogate(character))
            {
                return false;
            }

            if (!char.IsHighSurrogate(character))
            {
                continue;
            }

            if (index + 1 >= value.Length
                || !char.IsLowSurrogate(value[index + 1]))
            {
                return false;
            }

            index++;
        }

        return true;
    }
}

public sealed record DurableCommandFingerprint
{
    public DurableCommandFingerprint(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        if (value.Length != 64
            || value.Any(character => character is not (>= '0' and <= '9')
                and not (>= 'a' and <= 'f')))
        {
            throw new ArgumentException(
                "A Durable command fingerprint must be lowercase SHA-256 hexadecimal.",
                nameof(value));
        }

        Value = value;
    }

    public string Value { get; }
}

public sealed record DurableCommandReceiptKey
{
    public DurableCommandReceiptKey(
        WorkspaceId workspaceId,
        ulong attachmentGeneration,
        ClientIntentId clientIntentId,
        DurableCommandFingerprint commandFingerprint)
    {
        ArgumentNullException.ThrowIfNull(workspaceId);
        ArgumentOutOfRangeException.ThrowIfZero(attachmentGeneration);
        ArgumentNullException.ThrowIfNull(clientIntentId);
        ArgumentNullException.ThrowIfNull(commandFingerprint);
        WorkspaceId = workspaceId;
        AttachmentGeneration = attachmentGeneration;
        ClientIntentId = clientIntentId;
        CommandFingerprint = commandFingerprint;
    }

    public WorkspaceId WorkspaceId { get; }

    public ulong AttachmentGeneration { get; }

    public ClientIntentId ClientIntentId { get; }

    public DurableCommandFingerprint CommandFingerprint { get; }
}

public sealed record DurableProjectClaimRequest
{
    public DurableProjectClaimRequest(
        DurableProjectId durableProjectId,
        DurableVersion initialDurableVersion,
        AuthenticatedSubjectId subjectId,
        DurableDisplayName displayName,
        ProjectRevision projectRevision,
        DurableCommandReceiptKey receiptKey)
    {
        ArgumentNullException.ThrowIfNull(durableProjectId);
        ArgumentNullException.ThrowIfNull(initialDurableVersion);
        ArgumentNullException.ThrowIfNull(subjectId);
        ArgumentNullException.ThrowIfNull(displayName);
        ArgumentNullException.ThrowIfNull(projectRevision);
        ArgumentNullException.ThrowIfNull(receiptKey);
        DurableProjectId = durableProjectId;
        InitialDurableVersion = initialDurableVersion;
        SubjectId = subjectId;
        DisplayName = displayName;
        ProjectRevision = projectRevision;
        ReceiptKey = receiptKey;
        ClaimWorkspaceId = receiptKey.WorkspaceId;
    }

    public DurableProjectId DurableProjectId { get; }

    public DurableVersion InitialDurableVersion { get; }

    public AuthenticatedSubjectId SubjectId { get; }

    public DurableDisplayName DisplayName { get; }

    public ProjectRevision ProjectRevision { get; }

    public DurableCommandReceiptKey ReceiptKey { get; }

    public WorkspaceId ClaimWorkspaceId { get; }
}

public sealed record DurableProjectSaveRequest
{
    public DurableProjectSaveRequest(
        DurableProjectId durableProjectId,
        AuthenticatedSubjectId subjectId,
        DurableVersion expectedDurableVersion,
        DurableVersion nextDurableVersion,
        ProjectRevision projectRevision,
        DurableCommandReceiptKey receiptKey)
    {
        ArgumentNullException.ThrowIfNull(durableProjectId);
        ArgumentNullException.ThrowIfNull(subjectId);
        ArgumentNullException.ThrowIfNull(expectedDurableVersion);
        ArgumentNullException.ThrowIfNull(nextDurableVersion);
        ArgumentNullException.ThrowIfNull(projectRevision);
        ArgumentNullException.ThrowIfNull(receiptKey);
        DurableProjectId = durableProjectId;
        SubjectId = subjectId;
        ExpectedDurableVersion = expectedDurableVersion;
        NextDurableVersion = nextDurableVersion;
        ProjectRevision = projectRevision;
        ReceiptKey = receiptKey;
    }

    public DurableProjectId DurableProjectId { get; }

    public AuthenticatedSubjectId SubjectId { get; }

    public DurableVersion ExpectedDurableVersion { get; }

    public DurableVersion NextDurableVersion { get; }

    public ProjectRevision ProjectRevision { get; }

    public DurableCommandReceiptKey ReceiptKey { get; }
}

public abstract record DurableProjectClaimRepositoryOutcome
{
    private protected DurableProjectClaimRepositoryOutcome()
    {
    }
}

public sealed record DurableProjectClaimStored(
    DurableProjectId DurableProjectId,
    DurableVersion DurableVersion,
    ProjectRevisionId ProjectRevisionId,
    DurableDisplayName DisplayName) : DurableProjectClaimRepositoryOutcome;

public sealed record DurableProjectClaimReceiptConflict
    : DurableProjectClaimRepositoryOutcome;

public sealed record DurableProjectClaimForbidden
    : DurableProjectClaimRepositoryOutcome;

public abstract record DurableProjectSaveRepositoryOutcome
{
    private protected DurableProjectSaveRepositoryOutcome()
    {
    }
}

public sealed record DurableProjectSaveStored(
    DurableVersion DurableVersion,
    ProjectRevisionId ProjectRevisionId) : DurableProjectSaveRepositoryOutcome;

public sealed record DurableProjectSaveRepositoryConflict(
    DurableVersion ExpectedDurableVersion,
    DurableVersion ActualDurableVersion) : DurableProjectSaveRepositoryOutcome;

public sealed record DurableProjectSaveReceiptConflict
    : DurableProjectSaveRepositoryOutcome;

public sealed record DurableProjectSaveForbidden
    : DurableProjectSaveRepositoryOutcome;

public sealed class DurableProjectCommitUncertainException : Exception
{
    public DurableProjectCommitUncertainException(Exception innerException)
        : base(
            "The Durable Project transaction commit could not be confirmed.",
            innerException)
    {
        ArgumentNullException.ThrowIfNull(innerException);
    }
}

public interface IDurableProjectRepository
{
    Task<DurableProjectClaimRepositoryOutcome> ClaimAsync(
        DurableProjectClaimRequest request,
        CancellationToken cancellationToken);

    Task<DurableProjectClaimRepositoryOutcome?> TryReadClaimReceiptAsync(
        DurableProjectClaimRequest request,
        CancellationToken cancellationToken);

    Task<DurableProjectSaveRepositoryOutcome> SaveAsync(
        DurableProjectSaveRequest request,
        CancellationToken cancellationToken);

    Task<DurableProjectSaveRepositoryOutcome?> TryReadSaveReceiptAsync(
        DurableProjectSaveRequest request,
        CancellationToken cancellationToken);
}

public sealed record ClaimPrecondition
{
    public ClaimPrecondition(ProjectRevisionId projectRevisionId)
    {
        ArgumentNullException.ThrowIfNull(projectRevisionId);
        ProjectRevisionId = projectRevisionId;
    }

    public ProjectRevisionId ProjectRevisionId { get; }
}

public sealed record DurableSavePrecondition
{
    public DurableSavePrecondition(
        ProjectRevisionId projectRevisionId,
        DurableVersion expectedDurableVersion)
    {
        ArgumentNullException.ThrowIfNull(projectRevisionId);
        ArgumentNullException.ThrowIfNull(expectedDurableVersion);
        ProjectRevisionId = projectRevisionId;
        ExpectedDurableVersion = expectedDurableVersion;
    }

    public ProjectRevisionId ProjectRevisionId { get; }

    public DurableVersion ExpectedDurableVersion { get; }
}

public sealed record ClaimSandbox : WorkspaceCommand
{
    public ClaimSandbox(
        WorkspaceCommandContext context,
        ClaimPrecondition precondition,
        string requestedDisplayName)
        : base(context)
    {
        ArgumentNullException.ThrowIfNull(precondition);
        ArgumentNullException.ThrowIfNull(requestedDisplayName);
        Precondition = precondition;
        RequestedDisplayName = requestedDisplayName;
    }

    public ClaimPrecondition Precondition { get; }

    public string RequestedDisplayName { get; }
}

public sealed record SaveDurable : WorkspaceCommand
{
    public SaveDurable(
        WorkspaceCommandContext context,
        DurableSavePrecondition precondition)
        : base(context)
    {
        ArgumentNullException.ThrowIfNull(precondition);
        Precondition = precondition;
    }

    public DurableSavePrecondition Precondition { get; }
}

public sealed record DurableProjectClaimed(
    DurableProjectId DurableProjectId,
    DurableVersion DurableVersion,
    ProjectRevisionId ProjectRevisionId,
    DurableDisplayName DisplayName) : WorkspaceCommandOutcome;

public sealed record DurableProjectSaved(
    DurableVersion DurableVersion,
    ProjectRevisionId ProjectRevisionId) : WorkspaceCommandOutcome;

public enum DurableConflictRecovery
{
    Reload,
    Copy,
    Export,
}

public sealed record DurableProjectSaveConflict : WorkspaceCommandOutcome
{
    private static IReadOnlyList<DurableConflictRecovery> RecoveryValues { get; }
        = Array.AsReadOnly<DurableConflictRecovery>(
        [
            DurableConflictRecovery.Reload,
            DurableConflictRecovery.Copy,
            DurableConflictRecovery.Export,
        ]);

    public DurableProjectSaveConflict(
        DurableVersion expectedDurableVersion,
        DurableVersion actualDurableVersion)
    {
        ArgumentNullException.ThrowIfNull(expectedDurableVersion);
        ArgumentNullException.ThrowIfNull(actualDurableVersion);
        ExpectedDurableVersion = expectedDurableVersion;
        ActualDurableVersion = actualDurableVersion;
        Recovery = RecoveryValues;
    }

    public DurableVersion ExpectedDurableVersion { get; }

    public DurableVersion ActualDurableVersion { get; }

    public IReadOnlyList<DurableConflictRecovery> Recovery { get; }
}

public enum DurableSaveStatus
{
    Clean,
    Changed,
    Conflict,
}

public abstract record WorkspaceDurabilityProjection
{
    private protected WorkspaceDurabilityProjection()
    {
    }
}

public sealed record SandboxWorkspaceDurabilityProjection
    : WorkspaceDurabilityProjection
{
    private SandboxWorkspaceDurabilityProjection()
    {
    }

    public static SandboxWorkspaceDurabilityProjection Instance { get; } = new();
}

public sealed record DurableWorkspaceDurabilityProjection : WorkspaceDurabilityProjection
{
    public DurableWorkspaceDurabilityProjection(
        DurableProjectId durableProjectId,
        DurableVersion observedDurableVersion,
        ProjectRevisionId savedProjectRevisionId,
        DurableSaveStatus saveStatus,
        DurableVersion? conflictActualDurableVersion)
    {
        ArgumentNullException.ThrowIfNull(durableProjectId);
        ArgumentNullException.ThrowIfNull(observedDurableVersion);
        ArgumentNullException.ThrowIfNull(savedProjectRevisionId);
        if (!Enum.IsDefined(saveStatus)
            || (saveStatus == DurableSaveStatus.Conflict)
                != (conflictActualDurableVersion is not null))
        {
            throw new ArgumentException(
                "Durable save projection fields do not match its status.");
        }

        DurableProjectId = durableProjectId;
        ObservedDurableVersion = observedDurableVersion;
        SavedProjectRevisionId = savedProjectRevisionId;
        SaveStatus = saveStatus;
        ConflictActualDurableVersion = conflictActualDurableVersion;
    }

    public DurableProjectId DurableProjectId { get; }

    public DurableVersion ObservedDurableVersion { get; }

    public ProjectRevisionId SavedProjectRevisionId { get; }

    public DurableSaveStatus SaveStatus { get; }

    public DurableVersion? ConflictActualDurableVersion { get; }
}
