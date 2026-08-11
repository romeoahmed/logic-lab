using LogicLab.Engine.Compilation;
using LogicLab.Engine.Simulation;
using LogicLab.ProjectFormat;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LogicLab.Application.Workspaces;

public interface IEditorWorkspace : IAsyncDisposable
{
    Task<WorkspaceOpenOutcome> OpenAsync(
        OpenWorkspaceRequest request,
        CancellationToken cancellationToken);

    Task<WorkspaceAttachOutcome> AttachAsync(
        AttachRequest request,
        CancellationToken cancellationToken);

    Task<WorkspaceDetachOutcome> DetachAsync(
        DetachRequest request,
        CancellationToken cancellationToken);

    Task<WorkspaceCommandOutcome> DispatchAsync(
        WorkspaceCommand command,
        CancellationToken cancellationToken);

    Task<WorkspaceReadOutcome> ReadAsync(
        WorkspaceQueryContext context,
        WorkspaceQuery query,
        CancellationToken cancellationToken);
}

public sealed record WorkspaceAuthoringLimits
{
    public WorkspaceAuthoringLimits(
        int definitionCount,
        int entityCount,
        int commandItemCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(definitionCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(entityCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(commandItemCount);

        DefinitionCount = definitionCount;
        EntityCount = entityCount;
        CommandItemCount = commandItemCount;
    }

    public int DefinitionCount { get; }

    public int EntityCount { get; }

    public int CommandItemCount { get; }

    public static WorkspaceAuthoringLimits Default { get; } = new(
        definitionCount: 100,
        entityCount: 10_000,
        commandItemCount: 1_000);
}

public sealed record DurableDisplayNameLimits
{
    public DurableDisplayNameLimits(int scalarCount, int utf8Bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scalarCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(utf8Bytes);
        ScalarCount = scalarCount;
        Utf8Bytes = utf8Bytes;
    }

    public int ScalarCount { get; }

    public int Utf8Bytes { get; }

    public static DurableDisplayNameLimits Default { get; } = new(
        scalarCount: 128,
        utf8Bytes: 512);
}

public sealed record DurableProjectCatalogLimits
{
    public DurableProjectCatalogLimits(int pageItems, int cursorBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageItems);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cursorBytes);
        if (pageItems == int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pageItems),
                "The catalog must reserve one look-ahead item.");
        }

        PageItems = pageItems;
        CursorBytes = cursorBytes;
    }

    public int PageItems { get; }

    public int CursorBytes { get; }

    public static DurableProjectCatalogLimits Default { get; } = new(
        pageItems: 50,
        cursorBytes: 2_048);
}

public sealed record WorkspacePolicy
{
    public WorkspacePolicy(
        string policyId,
        string policyRevision,
        int globalWorkspaceLimit,
        TimeSpan sandboxRetention,
        WorkspaceAuthoringLimits authoringLimits,
        int historyRevisionCount,
        int idempotencyRecordCount,
        TimeSpan detachedRetention,
        ulong hotSwapPeakBytes,
        DurableDisplayNameLimits durableDisplayNameLimits,
        DurableProjectCatalogLimits durableProjectCatalogLimits)
    {
        ArgumentException.ThrowIfNullOrEmpty(policyId);
        ArgumentException.ThrowIfNullOrEmpty(policyRevision);
        ArgumentNullException.ThrowIfNull(authoringLimits);
        ArgumentNullException.ThrowIfNull(durableDisplayNameLimits);
        ArgumentNullException.ThrowIfNull(durableProjectCatalogLimits);
        if (!IsStableToken(policyId))
        {
            throw new ArgumentException(
                "The Workspace Policy ID must be a Stable Token.",
                nameof(policyId));
        }

        if (!IsStableToken(policyRevision))
        {
            throw new ArgumentException(
                "The Workspace Policy revision must be a Stable Token.",
                nameof(policyRevision));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(globalWorkspaceLimit);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            sandboxRetention,
            TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(historyRevisionCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(idempotencyRecordCount);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            detachedRetention,
            TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfZero(hotSwapPeakBytes);

        PolicyId = policyId;
        PolicyRevision = policyRevision;
        GlobalWorkspaceLimit = globalWorkspaceLimit;
        SandboxRetention = sandboxRetention;
        AuthoringLimits = authoringLimits;
        HistoryRevisionCount = historyRevisionCount;
        IdempotencyRecordCount = idempotencyRecordCount;
        DetachedRetention = detachedRetention;
        HotSwapPeakBytes = hotSwapPeakBytes;
        DurableDisplayNameLimits = durableDisplayNameLimits;
        DurableProjectCatalogLimits = durableProjectCatalogLimits;
    }

    public string PolicyId { get; }

    public string PolicyRevision { get; }

    public int GlobalWorkspaceLimit { get; }

    public TimeSpan SandboxRetention { get; }

    public WorkspaceAuthoringLimits AuthoringLimits { get; }

    public int HistoryRevisionCount { get; }

    public int IdempotencyRecordCount { get; }

    public TimeSpan DetachedRetention { get; }

    public ulong HotSwapPeakBytes { get; }

    public DurableDisplayNameLimits DurableDisplayNameLimits { get; }

    public DurableProjectCatalogLimits DurableProjectCatalogLimits { get; }

    public static WorkspacePolicy Default { get; } = new(
        policyId: "workbench-workspace",
        policyRevision: "1",
        globalWorkspaceLimit: 128,
        sandboxRetention: TimeSpan.FromMinutes(30),
        authoringLimits: WorkspaceAuthoringLimits.Default,
        historyRevisionCount: 128,
        idempotencyRecordCount: 1_024,
        detachedRetention: TimeSpan.FromMinutes(30),
        hotSwapPeakBytes: 512UL * 1024UL * 1024UL,
        durableDisplayNameLimits: DurableDisplayNameLimits.Default,
        durableProjectCatalogLimits: DurableProjectCatalogLimits.Default);

    private static bool IsStableToken(string value)
    {
        return value.Length <= 96
            && IsAsciiLetterOrDigit(value[0])
            && value.All(character =>
                IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-');
    }

    private static bool IsAsciiLetterOrDigit(char value)
    {
        return value is >= 'A' and <= 'Z'
            or >= 'a' and <= 'z'
            or >= '0' and <= '9';
    }
}

public sealed record SchedulingPolicy
{
    public SchedulingPolicy(int compilationQueueCapacity, int sessionQueueCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(compilationQueueCapacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sessionQueueCapacity);
        CompilationQueueCapacity = compilationQueueCapacity;
        SessionQueueCapacity = sessionQueueCapacity;
    }

    public int CompilationQueueCapacity { get; }

    public int SessionQueueCapacity { get; }

    public static SchedulingPolicy Default { get; } = new(
        compilationQueueCapacity: 16,
        sessionQueueCapacity: 64);
}

public static class EditorWorkspaceFactory
{
    public static IEditorWorkspace Create(
        string buildFingerprint,
        IDurableProjectRepository durableProjectRepository,
        IDurableProjectLoader durableProjectLoader,
        IProjectExportStore projectExportStore,
        WorkspacePolicy? workspacePolicy = null,
        SchedulingPolicy? schedulingPolicy = null,
        TimeProvider? timeProvider = null,
        ILoggerFactory? loggerFactory = null,
        PackagePolicy? packagePolicy = null)
    {
        ArgumentNullException.ThrowIfNull(durableProjectRepository);
        ArgumentNullException.ThrowIfNull(durableProjectLoader);
        ArgumentNullException.ThrowIfNull(projectExportStore);
        return CreateCore(
            workspacePolicy,
            schedulingPolicy,
            timeProvider,
            loggerFactory,
            buildFingerprint,
            WorkspaceModuleOperations.Production,
            durableProjectRepository,
            durableProjectLoader,
            packagePolicy,
            projectExportStore);
    }

    internal static IEditorWorkspace CreateForTesting(
        WorkspaceModuleOperations operations,
        IDurableProjectRepository durableProjectRepository,
        IDurableProjectLoader durableProjectLoader,
        IProjectExportStore projectExportStore,
        WorkspacePolicy? workspacePolicy = null,
        SchedulingPolicy? schedulingPolicy = null,
        TimeProvider? timeProvider = null,
        ILoggerFactory? loggerFactory = null,
        string buildFingerprint = WorkspaceBuild.DevelopmentFingerprint,
        PackagePolicy? packagePolicy = null)
    {
        ArgumentNullException.ThrowIfNull(durableProjectRepository);
        ArgumentNullException.ThrowIfNull(durableProjectLoader);
        ArgumentNullException.ThrowIfNull(projectExportStore);
        return CreateCore(
            workspacePolicy,
            schedulingPolicy,
            timeProvider,
            loggerFactory,
            buildFingerprint,
            operations,
            durableProjectRepository,
            durableProjectLoader,
            packagePolicy,
            projectExportStore);
    }

    private static EditorWorkspace CreateCore(
        WorkspacePolicy? workspacePolicy,
        SchedulingPolicy? schedulingPolicy,
        TimeProvider? timeProvider,
        ILoggerFactory? loggerFactory,
        string buildFingerprint,
        WorkspaceModuleOperations operations,
        IDurableProjectRepository durableProjectRepository,
        IDurableProjectLoader durableProjectLoader,
        PackagePolicy? packagePolicy,
        IProjectExportStore projectExportStore)
    {
        ArgumentException.ThrowIfNullOrEmpty(buildFingerprint);
        var resolvedLoggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        return new EditorWorkspace(
            schedulingPolicy ?? SchedulingPolicy.Default,
            workspacePolicy ?? WorkspacePolicy.Default,
            timeProvider ?? TimeProvider.System,
            buildFingerprint,
            operations,
            durableProjectRepository,
            durableProjectLoader,
            packagePolicy ?? PackagePolicy.Development,
            projectExportStore,
            resolvedLoggerFactory.CreateLogger<Work.WorkCoordinator>(),
            resolvedLoggerFactory.CreateLogger<EditorWorkspace>());
    }
}

internal sealed record WorkspaceModuleOperations(
    Func<CompilationRequest, CancellationToken, CompilationOutcome> Compile,
    Func<OpenSimulationRequest, CancellationToken, SimulationOpenOutcome> OpenSimulation,
    Func<SimulationSessionHandle, SimulationCommand, CancellationToken, SimulationCommandOutcome>
        ExecuteSimulation,
    Func<SimulationSessionHandle, SimulationQuery, CancellationToken, SimulationReadOutcome>
        ReadSimulation,
    Func<SimulationSessionHandle, CloseSimulationOutcome> CloseSimulation,
    Func<ProjectPackageWriteRequest, CancellationToken, Task<PackageWriteOutcome>>
        WritePackage)
{
    public static WorkspaceModuleOperations Production { get; } = new(
        Compiler.Compile,
        SimulationRuntime.Open,
        SimulationRuntime.Execute,
        SimulationRuntime.Read,
        SimulationRuntime.Close,
        ProjectPackage.WriteAsync);
}
