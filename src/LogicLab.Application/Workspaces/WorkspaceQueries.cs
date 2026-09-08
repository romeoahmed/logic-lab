using System.Collections.ObjectModel;

namespace LogicLab.Application.Workspaces;

public abstract record WorkspaceQuery
{
    private protected WorkspaceQuery()
    {
    }
}

public sealed record ReadProjection : WorkspaceQuery
{
    public ReadProjection(ulong? afterProjectionVersion = null)
    {
        if (afterProjectionVersion is 0)
        {
            throw new ArgumentOutOfRangeException(nameof(afterProjectionVersion));
        }

        AfterProjectionVersion = afterProjectionVersion;
    }

    public static ReadProjection Instance { get; } = new();

    public ulong? AfterProjectionVersion { get; }
}

public sealed record ReadCompilation : WorkspaceQuery
{
    public ReadCompilation(CompilationGeneration compilationGeneration)
    {
        ArgumentNullException.ThrowIfNull(compilationGeneration);
        CompilationGeneration = compilationGeneration;
    }

    public CompilationGeneration CompilationGeneration { get; }
}

public abstract record WorkspaceReadOutcome
{
    private protected WorkspaceReadOutcome()
    {
    }
}

public sealed record ProjectionSnapshot(WorkspaceProjection Projection)
    : WorkspaceReadOutcome;

public sealed record ProjectionUnchanged : WorkspaceReadOutcome
{
    public ProjectionUnchanged(ulong projectionVersion)
    {
        ArgumentOutOfRangeException.ThrowIfZero(projectionVersion);
        ProjectionVersion = projectionVersion;
    }

    public ulong ProjectionVersion { get; }
}

public sealed record CompilationSnapshot : WorkspaceReadOutcome
{
    public CompilationSnapshot(
        CompilationProjection compilation,
        ulong projectionVersion)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        ArgumentOutOfRangeException.ThrowIfZero(projectionVersion);
        if (compilation.Generation is null)
        {
            throw new ArgumentException(
                "A compilation snapshot requires a Compilation Generation.",
                nameof(compilation));
        }

        Compilation = compilation;
        ProjectionVersion = projectionVersion;
    }

    public CompilationProjection Compilation { get; }

    public ulong ProjectionVersion { get; }
}

public sealed record WorkspaceReadRejected : WorkspaceReadOutcome
{
    public WorkspaceReadRejected(
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
