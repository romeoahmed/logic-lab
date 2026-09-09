using LogicLab.Domain.Authoring;
using LogicLab.Engine.Compilation;

namespace LogicLab.Application.Workspaces;

public enum WorkspaceNoticeSeverity
{
    Info,
    Warning,
}

/// <summary>A structured Workspace notice, separate from operation rejection.</summary>
public abstract record WorkspaceNotice
{
    private protected WorkspaceNotice()
    {
    }

    public abstract string Code { get; }

    public abstract WorkspaceNoticeSeverity Severity { get; }
}

public sealed record WorkspaceAttachmentRecovered : WorkspaceNotice
{
    private WorkspaceAttachmentRecovered()
    {
    }

    public static WorkspaceAttachmentRecovered Instance { get; } = new();

    public override string Code => "workspace_attachment_recovered";

    public override WorkspaceNoticeSeverity Severity => WorkspaceNoticeSeverity.Info;
}

public sealed record WorkspaceCompilationStale : WorkspaceNotice
{
    private WorkspaceCompilationStale()
    {
    }

    public static WorkspaceCompilationStale Instance { get; } = new();

    public override string Code => "workspace_compilation_stale";

    public override WorkspaceNoticeSeverity Severity => WorkspaceNoticeSeverity.Warning;
}

public sealed record WorkspaceHistoryTruncated : WorkspaceNotice
{
    public WorkspaceHistoryTruncated(ulong removedRevisions)
    {
        ArgumentOutOfRangeException.ThrowIfZero(removedRevisions);
        RemovedRevisions = removedRevisions;
    }

    public ulong RemovedRevisions { get; }

    public override string Code => "workspace_history_truncated";

    public override WorkspaceNoticeSeverity Severity => WorkspaceNoticeSeverity.Info;
}

public sealed record WorkspaceProbeUnresolved : WorkspaceNotice
{
    public WorkspaceProbeUnresolved(CompilationSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Identity is not NetSourceIdentity)
        {
            throw new ArgumentException("A Probe notice must identify an authored Net.", nameof(source));
        }
        Source = source;
    }

    public CompilationSource Source { get; }

    public const string Rule = "artifactIncompatible";

    public override string Code => "workspace_probe_unresolved";

    public override WorkspaceNoticeSeverity Severity => WorkspaceNoticeSeverity.Warning;
}
