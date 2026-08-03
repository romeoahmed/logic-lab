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
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(globalWorkspaceLimit);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            sandboxRetention,
            TimeSpan.Zero);
        ArgumentNullException.ThrowIfNull(authoringLimits);

        GlobalWorkspaceLimit = globalWorkspaceLimit;
        SandboxRetention = sandboxRetention;
        AuthoringLimits = authoringLimits;
    }

    public int GlobalWorkspaceLimit { get; }

    public TimeSpan SandboxRetention { get; }

    public WorkspaceAuthoringLimits AuthoringLimits { get; }

    public static WorkspacePolicy Default { get; } = new(
        globalWorkspaceLimit: 128,
        sandboxRetention: TimeSpan.FromMinutes(30),
        WorkspaceAuthoringLimits.Default);
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
        ILoggerFactory? loggerFactory = null)
    {
        return CreateCore(
            workspacePolicy,
            schedulingPolicy,
            timeProvider,
            loggerFactory,
            WorkspaceModuleOperations.Production);
    }

    internal static IEditorWorkspace CreateForTesting(
        WorkspacePolicy? workspacePolicy = null,
        SchedulingPolicy? schedulingPolicy = null,
        TimeProvider? timeProvider = null,
        ILoggerFactory? loggerFactory = null,
        WorkspaceModuleOperations? operations = null)
    {
        return CreateCore(
            workspacePolicy,
            schedulingPolicy,
            timeProvider,
            loggerFactory,
            operations ?? WorkspaceModuleOperations.Production);
    }

    private static EditorWorkspace CreateCore(
        WorkspacePolicy? workspacePolicy,
        SchedulingPolicy? schedulingPolicy,
        TimeProvider? timeProvider,
        ILoggerFactory? loggerFactory,
        WorkspaceModuleOperations operations)
    {
        var resolvedLoggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        var coordinator = new Work.WorkCoordinator(
            schedulingPolicy ?? SchedulingPolicy.Default,
            resolvedLoggerFactory.CreateLogger<Work.WorkCoordinator>());
        return new EditorWorkspace(
            coordinator,
            workspacePolicy ?? WorkspacePolicy.Default,
            timeProvider ?? TimeProvider.System,
            operations,
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
