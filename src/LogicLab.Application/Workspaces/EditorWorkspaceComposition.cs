using LogicLab.Engine.Compilation;
using LogicLab.Engine.Simulation;
using LogicLab.ProjectFormat;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LogicLab.Application.Workspaces;

/// <summary>
/// Coordinates authoring, compilation, and simulation for caller-bound workspaces.
/// Concurrent calls are admitted and serialized by operation; attachments and command
/// preconditions determine whether their results can be published.
/// </summary>
/// <remarks>
/// Asynchronous disposal closes admission and drains accepted calls and background work
/// before releasing workspace resources. Repeated disposal awaits the same completion.
/// </remarks>
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

public interface IEditorWorkspaceReadiness
{
    bool IsReady { get; }
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
        string buildFingerprint = WorkspaceBuild.TestFingerprint,
        PackagePolicy? packagePolicy = null)
    {
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
        var resolvedLoggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        return new EditorWorkspace(
            schedulingPolicy ?? SchedulingPolicy.Default,
            workspacePolicy ?? WorkspacePolicy.Default,
            timeProvider ?? TimeProvider.System,
            buildFingerprint,
            operations,
            durableProjectRepository,
            durableProjectLoader,
            packagePolicy ?? PackagePolicy.Default,
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
