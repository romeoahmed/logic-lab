using LogicLab.Engine.Compilation;
using LogicLab.Engine.Simulation;
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
        WorkspaceId workspaceId,
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

public sealed record WorkspacePolicy
{
    public WorkspacePolicy(
        int globalWorkspaceLimit,
        TimeSpan sandboxRetention,
        WorkspaceAuthoringLimits authoringLimits)
        : this(
            globalWorkspaceLimit,
            sandboxRetention,
            authoringLimits,
            historyRevisionCount: 128,
            idempotencyRecordCount: 1_024,
            detachedRetention: sandboxRetention)
    {
    }

    public WorkspacePolicy(
        int globalWorkspaceLimit,
        TimeSpan sandboxRetention,
        WorkspaceAuthoringLimits authoringLimits,
        int historyRevisionCount,
        int idempotencyRecordCount,
        TimeSpan detachedRetention)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(globalWorkspaceLimit);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            sandboxRetention,
            TimeSpan.Zero);
        ArgumentNullException.ThrowIfNull(authoringLimits);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(historyRevisionCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(idempotencyRecordCount);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            detachedRetention,
            TimeSpan.Zero);

        GlobalWorkspaceLimit = globalWorkspaceLimit;
        SandboxRetention = sandboxRetention;
        AuthoringLimits = authoringLimits;
        HistoryRevisionCount = historyRevisionCount;
        IdempotencyRecordCount = idempotencyRecordCount;
        DetachedRetention = detachedRetention;
    }

    public int GlobalWorkspaceLimit { get; }

    public TimeSpan SandboxRetention { get; }

    public WorkspaceAuthoringLimits AuthoringLimits { get; }

    public int HistoryRevisionCount { get; }

    public int IdempotencyRecordCount { get; }

    public TimeSpan DetachedRetention { get; }

    public static WorkspacePolicy Default { get; } = new(
        globalWorkspaceLimit: 128,
        sandboxRetention: TimeSpan.FromMinutes(30),
        authoringLimits: WorkspaceAuthoringLimits.Default);
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
        WorkspacePolicy? workspacePolicy = null,
        SchedulingPolicy? schedulingPolicy = null,
        TimeProvider? timeProvider = null,
        ILoggerFactory? loggerFactory = null,
        string buildFingerprint = WorkspaceBuild.DevelopmentFingerprint)
    {
        return CreateCore(
            workspacePolicy,
            schedulingPolicy,
            timeProvider,
            loggerFactory,
            buildFingerprint,
            WorkspaceModuleOperations.Production);
    }

    internal static IEditorWorkspace CreateForTesting(
        WorkspaceModuleOperations operations,
        WorkspacePolicy? workspacePolicy = null,
        SchedulingPolicy? schedulingPolicy = null,
        TimeProvider? timeProvider = null,
        ILoggerFactory? loggerFactory = null,
        string buildFingerprint = WorkspaceBuild.DevelopmentFingerprint)
    {
        return CreateCore(
            workspacePolicy,
            schedulingPolicy,
            timeProvider,
            loggerFactory,
            buildFingerprint,
            operations);
    }

    private static EditorWorkspace CreateCore(
        WorkspacePolicy? workspacePolicy,
        SchedulingPolicy? schedulingPolicy,
        TimeProvider? timeProvider,
        ILoggerFactory? loggerFactory,
        string buildFingerprint,
        WorkspaceModuleOperations operations)
    {
        ArgumentException.ThrowIfNullOrEmpty(buildFingerprint);
        var resolvedLoggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        return new EditorWorkspace(
            schedulingPolicy ?? SchedulingPolicy.Default,
            workspacePolicy ?? WorkspacePolicy.Default,
            timeProvider ?? TimeProvider.System,
            buildFingerprint,
            operations,
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
    Func<SimulationSessionHandle, CloseSimulationOutcome> CloseSimulation)
{
    public static WorkspaceModuleOperations Production { get; } = new(
        Compiler.Compile,
        SimulationRuntime.Open,
        SimulationRuntime.Execute,
        SimulationRuntime.Read,
        SimulationRuntime.Close);
}
