namespace LogicLab.Infrastructure.Persistence;

internal sealed class DurableProjectRecord
{
    public required string Id { get; set; }

    public required string ClaimWorkspaceId { get; set; }

    public required string SubjectId { get; set; }

    public required string DisplayName { get; set; }

    public required byte[] DisplayNameSortKey { get; set; }

    public required string CurrentProjectRevisionId { get; set; }

    public required string DurableVersion { get; set; }
}

internal sealed class ProjectRevisionRecord
{
    public required string DurableProjectId { get; set; }

    public required string ProjectRevisionId { get; set; }

    public required byte[] Payload { get; set; }
}

internal sealed class DurableCommandReceiptRecord
{
    public long ReceiptSequence { get; set; }

    public required string WorkspaceId { get; set; }

    public required string AttachmentGeneration { get; set; }

    public required string ClientIntentId { get; set; }

    public required string CommandFingerprint { get; set; }

    public required string CommandKind { get; set; }

    public required string OutcomeKind { get; set; }

    public required string DurableProjectId { get; set; }

    public required string DurableVersion { get; set; }

    public string? ProjectRevisionId { get; set; }

    public string? ExpectedDurableVersion { get; set; }

    public string? ActualDurableVersion { get; set; }
}
