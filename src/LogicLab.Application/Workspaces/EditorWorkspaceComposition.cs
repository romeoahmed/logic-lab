using System.Collections.ObjectModel;
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
        int workspaceCountPerSubject,
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
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(workspaceCountPerSubject);
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
        WorkspaceCountPerSubject = workspaceCountPerSubject;
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

    public int WorkspaceCountPerSubject { get; }

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
        workspaceCountPerSubject: 8,
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

public enum SchedulingDimension
{
    AdmissionRequestsGlobal,
    AdmissionRequestsPerSubject,
    AdmissionPartitionCount,
    AdmissionWindowMilliseconds,
    CompilationQueueItems,
    SessionQueueItems,
    AnalysisQueueItems,
    AnalysisQueueItemsPerSubject,
    CompilationWorkerCount,
    SessionWorkerCount,
    AnalysisWorkerCount,
    AnalysisResultRetentionSeconds,
}

public sealed record SchedulingLimit(SchedulingDimension Dimension, ulong Maximum);

public sealed class SchedulingPolicy
{
    private readonly SchedulingLimit[] limits;

    public SchedulingPolicy(
        string policyId,
        string policyRevision,
        IReadOnlyList<SchedulingLimit> limits)
    {
        ArgumentException.ThrowIfNullOrEmpty(policyId);
        ArgumentException.ThrowIfNullOrEmpty(policyRevision);
        ArgumentNullException.ThrowIfNull(limits);
        if (!IsStableToken(policyId))
        {
            throw new ArgumentException(
                "The Scheduling Policy ID must be a Stable Token.",
                nameof(policyId));
        }

        if (!IsStableToken(policyRevision))
        {
            throw new ArgumentException(
                "The Scheduling Policy revision must be a Stable Token.",
                nameof(policyRevision));
        }

        var dimensions = Enum.GetValues<SchedulingDimension>();
        if (limits.Count != dimensions.Length)
        {
            throw new ArgumentException(
                "A Scheduling Policy must define every dimension exactly once.",
                nameof(limits));
        }

        var ownedLimits = limits.ToArray();
        for (var index = 0; index < dimensions.Length; index++)
        {
            if (ownedLimits[index] is not { } limit
                || limit.Dimension != dimensions[index]
                || limit.Maximum == 0)
            {
                throw new ArgumentException(
                    "Scheduling Policy limits must be positive and in canonical dimension order.",
                    nameof(limits));
            }
        }

        this.limits = ownedLimits;
        PolicyId = policyId;
        PolicyRevision = policyRevision;
        Limits = Array.AsReadOnly(this.limits);
    }

    public string PolicyId { get; }

    public string PolicyRevision { get; }

    public ReadOnlyCollection<SchedulingLimit> Limits { get; }

    public static SchedulingPolicy Default { get; } = new(
        "workbench-scheduling",
        "1",
        [
            new(SchedulingDimension.AdmissionRequestsGlobal, 4_096),
            new(SchedulingDimension.AdmissionRequestsPerSubject, 256),
            new(SchedulingDimension.AdmissionPartitionCount, 1_024),
            new(SchedulingDimension.AdmissionWindowMilliseconds, 1_000),
            new(SchedulingDimension.CompilationQueueItems, 16),
            new(SchedulingDimension.SessionQueueItems, 64),
            new(SchedulingDimension.AnalysisQueueItems, 64),
            new(SchedulingDimension.AnalysisQueueItemsPerSubject, 8),
            new(SchedulingDimension.CompilationWorkerCount, 1),
            new(SchedulingDimension.SessionWorkerCount, 1),
            new(SchedulingDimension.AnalysisWorkerCount, 1),
            new(SchedulingDimension.AnalysisResultRetentionSeconds, 300),
        ]);

    public ulong GetMaximum(SchedulingDimension dimension)
    {
        if (!Enum.IsDefined(dimension))
        {
            throw new ArgumentOutOfRangeException(nameof(dimension));
        }

        return limits[(int)dimension].Maximum;
    }

    internal PolicyEvidenceProjection Evidence(
        SchedulingDimension dimension,
        ulong observed)
    {
        return new PolicyEvidenceProjection(
            PolicyId,
            PolicyRevision,
            DimensionToken(dimension),
            observed);
    }

    internal static string DimensionToken(SchedulingDimension dimension)
    {
        return dimension switch
        {
            SchedulingDimension.AdmissionRequestsGlobal =>
                "admission_requests_global",
            SchedulingDimension.AdmissionRequestsPerSubject =>
                "admission_requests_per_subject",
            SchedulingDimension.AdmissionPartitionCount =>
                "admission_partition_count",
            SchedulingDimension.AdmissionWindowMilliseconds =>
                "admission_window_milliseconds",
            SchedulingDimension.CompilationQueueItems => "compilation_queue_items",
            SchedulingDimension.SessionQueueItems => "session_queue_items",
            SchedulingDimension.AnalysisQueueItems => "analysis_queue_items",
            SchedulingDimension.AnalysisQueueItemsPerSubject =>
                "analysis_queue_items_per_subject",
            SchedulingDimension.CompilationWorkerCount => "compilation_worker_count",
            SchedulingDimension.SessionWorkerCount => "session_worker_count",
            SchedulingDimension.AnalysisWorkerCount => "analysis_worker_count",
            SchedulingDimension.AnalysisResultRetentionSeconds =>
                "analysis_result_retention_seconds",
            _ => throw new ArgumentOutOfRangeException(nameof(dimension)),
        };
    }

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

public static class EditorWorkspaceFactory
{
    public static IEditorWorkspace Create(
        string buildFingerprint,
        IDurableProjectRepository durableProjectRepository,
        IDurableProjectLoader durableProjectLoader,
        IProjectExportStore projectExportStore,
        WorkspacePolicy? workspacePolicy = null,
        SchedulingPolicy? schedulingPolicy = null,
        ProjectExportPreparationPolicy? projectExportPreparationPolicy = null,
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
            projectExportPreparationPolicy,
            projectExportStore);
    }

    internal static IEditorWorkspace CreateForTesting(
        WorkspaceModuleOperations operations,
        IDurableProjectRepository durableProjectRepository,
        IDurableProjectLoader durableProjectLoader,
        IProjectExportStore projectExportStore,
        WorkspacePolicy? workspacePolicy = null,
        SchedulingPolicy? schedulingPolicy = null,
        ProjectExportPreparationPolicy? projectExportPreparationPolicy = null,
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
            projectExportPreparationPolicy,
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
        ProjectExportPreparationPolicy? projectExportPreparationPolicy,
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
            projectExportPreparationPolicy ?? ProjectExportPreparationPolicy.Default,
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
