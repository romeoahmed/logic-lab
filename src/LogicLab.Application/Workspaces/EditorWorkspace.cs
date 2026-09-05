using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using LogicLab.Application.Work;
using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.Engine;
using LogicLab.Engine.Compilation;
using LogicLab.Engine.Simulation;
using LogicLab.ProjectFormat;
using Microsoft.Extensions.Logging;

namespace LogicLab.Application.Workspaces;

internal sealed partial class EditorWorkspace : IEditorWorkspace, IEditorWorkspaceReadiness
{
    private readonly Lock gate = new();
    private readonly Lock operationAdmissionGate = new();
    private readonly Lock projectExportPreparationAdmissionGate = new();
    private readonly Dictionary<WorkspaceId, WorkspaceState> workspaces = [];
    private readonly Dictionary<WorkspaceCaller, int> workspaceCountsByCaller = [];
    private readonly WorkCoordinator workCoordinator;
    private readonly WorkspacePolicy workspacePolicy;
    private readonly TimeProvider timeProvider;
    private readonly string buildFingerprint;
    private readonly WorkspaceModuleOperations operations;
    private readonly IDurableProjectRepository durableProjectRepository;
    private readonly IDurableProjectLoader durableProjectLoader;
    private readonly PackagePolicy packagePolicy;
    private readonly IProjectExportStore projectExportStore;
    private readonly int maximumConcurrentProjectExportPreparations;
    private readonly ILogger<EditorWorkspace> logger;
    private int activeProjectExportPreparations;
    private int workspaceReservations;
    private int anonymousWorkspaceCount;
    private int activeOperations;
    private TaskCompletionSource? operationsDrained;
    private Task? disposalTask;
    private bool operationAdmissionClosed;
    private bool isDisposed;

    public bool IsReady
    {
        get
        {
            lock (operationAdmissionGate)
            {
                return !operationAdmissionClosed;
            }
        }
    }

    public EditorWorkspace(
        SchedulingPolicy schedulingPolicy,
        WorkspacePolicy workspacePolicy,
        TimeProvider timeProvider,
        string buildFingerprint,
        WorkspaceModuleOperations operations,
        IDurableProjectRepository durableProjectRepository,
        IDurableProjectLoader durableProjectLoader,
        PackagePolicy packagePolicy,
        ProjectExportPreparationPolicy projectExportPreparationPolicy,
        IProjectExportStore projectExportStore,
        ILogger<WorkCoordinator> workCoordinatorLogger,
        ILogger<EditorWorkspace> logger)
    {
        ArgumentNullException.ThrowIfNull(schedulingPolicy);
        ArgumentNullException.ThrowIfNull(workspacePolicy);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentException.ThrowIfNullOrEmpty(buildFingerprint);
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(durableProjectRepository);
        ArgumentNullException.ThrowIfNull(durableProjectLoader);
        ArgumentNullException.ThrowIfNull(packagePolicy);
        ArgumentNullException.ThrowIfNull(projectExportPreparationPolicy);
        ArgumentNullException.ThrowIfNull(projectExportStore);
        ArgumentNullException.ThrowIfNull(workCoordinatorLogger);
        ArgumentNullException.ThrowIfNull(logger);
        workCoordinator = new WorkCoordinator(
            schedulingPolicy,
            timeProvider,
            workCoordinatorLogger);
        this.workspacePolicy = workspacePolicy;
        this.timeProvider = timeProvider;
        this.buildFingerprint = buildFingerprint;
        this.operations = operations;
        this.durableProjectRepository = durableProjectRepository;
        this.durableProjectLoader = durableProjectLoader;
        this.packagePolicy = packagePolicy;
        maximumConcurrentProjectExportPreparations =
            projectExportPreparationPolicy.MaximumConcurrentPreparations;
        this.projectExportStore = projectExportStore;
        this.logger = logger;
    }

    public async Task<WorkspaceOpenOutcome> OpenAsync(
        OpenWorkspaceRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var operation = TryEnterOperation();
        if (operation is null)
        {
            return RejectOpen(WorkspaceOutcomeReasons.WorkspaceCancelled);
        }

        return await OpenCoreAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private Task<WorkspaceOpenOutcome> OpenCoreAsync(
        OpenWorkspaceRequest request,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult<WorkspaceOpenOutcome>(
                RejectOpen(WorkspaceOutcomeReasons.WorkspaceCancelled));
        }

        if (request is CopyWorkspace copy)
        {
            return CopyAsync(copy, cancellationToken);
        }

        if (request is OpenDurable or ImportProject)
        {
            return OpenCompiledWorkspaceAsync(request, cancellationToken);
        }

        if (request is not CreateSandbox create)
        {
            return Task.FromResult<WorkspaceOpenOutcome>(
                RejectOpen(WorkspaceOutcomeReasons.WorkspaceInternalDefect));
        }

        var rejectionReason = ReserveWorkspace(
            create.Caller,
            out var retired,
            out var policyEvidence);
        RetireAll(retired);
        if (rejectionReason is not null)
        {
            return Task.FromResult<WorkspaceOpenOutcome>(
                RejectOpen(rejectionReason, policyEvidence: policyEvidence));
        }

        var hasReservation = true;
        try
        {
            var genesis = ProjectEditor.Begin(new NewProjectSeed(
                create.ProjectDisplayName,
                LibrarySnapshot.Core,
                new SymbolProfileReference(
                    "TeachingMixed",
                    "1.0.0",
                    IndicationConvention.Negation),
                create.EntryCircuitDefinitionDisplayName));
            if (genesis is ProjectGenesisRejected rejected)
            {
                return Task.FromResult<WorkspaceOpenOutcome>(RejectOpen(
                    rejected.Reason,
                    [.. rejected.Diagnostics.Select(item => item.Code)]));
            }

            var committed = (ProjectGenesisCommitted)genesis;
            var id = WorkspaceId.Create();
            var state = new WorkspaceState(
                id,
                committed.Revision,
                create.Caller,
                timeProvider.GetTimestamp());
            lock (gate)
            {
                hasReservation = false;
                rejectionReason = PublishWorkspaceReservationUnderLock(
                    state,
                    cancellationToken);
            }

            if (rejectionReason is not null)
            {
                DisposeUnpublishedWorkspace(state);
                return Task.FromResult<WorkspaceOpenOutcome>(
                    RejectOpen(rejectionReason));
            }

            return Task.FromResult<WorkspaceOpenOutcome>(
                new WorkspaceOpened(id, Project(state)));
        }
        finally
        {
            if (hasReservation)
            {
                ReleaseWorkspaceReservation(create.Caller);
            }
        }
    }

    public async Task<WorkspaceCommandOutcome> DispatchAsync(
        WorkspaceCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        using var operation = TryEnterOperation();
        if (operation is null)
        {
            return Reject(WorkspaceOutcomeReasons.WorkspaceCancelled);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Reject(WorkspaceOutcomeReasons.WorkspaceCancelled);
        }

        if (command is ClaimSandbox or SaveDurable
            && command.Context.Caller is not AuthenticatedWorkspaceCaller)
        {
            return Reject(WorkspaceOutcomeReasons.AuthenticationRequired);
        }

        using var acquisition = AcquireWorkspace(command.WorkspaceId);
        if (!acquisition.IsAcquired)
        {
            if (command is CloseWorkspace
                && acquisition.RejectionReason is WorkspaceOutcomeReasons.WorkspaceNotFound)
            {
                return new WorkspaceClosed(command.WorkspaceId);
            }

            return Reject(acquisition.RejectionReason);
        }

        var state = acquisition.State;

        return command switch
        {
            ClaimSandbox or SaveDurable => await ExecuteDurableCommandAsync(
                state,
                command,
                cancellationToken).ConfigureAwait(false),
            PrepareExport prepare => await ExecutePrepareExportAsync(
                state,
                prepare,
                cancellationToken).ConfigureAwait(false),
            RequestCompilation request => await QueueCompilationAsync(
                state,
                request,
                cancellationToken).ConfigureAwait(false),
            CreateSession or RestartSession or CloseSession
                or ScheduleStimulusBatch or StepSession or ReplaceProbes
                or StartRun or HotSwapSession =>
                await QueueContextualSessionAsync(
                    state,
                    command,
                    cancellationToken).ConfigureAwait(false),
            PauseRun pause => await QueueRunPauseAsync(
                state,
                pause,
                cancellationToken).ConfigureAwait(false),
            _ => await ExecuteContextualCommandAsync(
                state,
                command,
                cancellationToken).ConfigureAwait(false),
        };
    }

    public async Task<WorkspaceReadOutcome> ReadAsync(
        WorkspaceQueryContext context,
        WorkspaceQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(query);
        using var operation = TryEnterOperation();
        if (operation is null)
        {
            return RejectRead(WorkspaceOutcomeReasons.WorkspaceCancelled);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return RejectRead(WorkspaceOutcomeReasons.WorkspaceCancelled);
        }

        using var acquisition = AcquireWorkspace(context.WorkspaceId);
        if (!acquisition.IsAcquired)
        {
            return RejectRead(acquisition.RejectionReason);
        }

        var state = acquisition.State;
        var admissionRejection = await EnterAuthorizedCommandGateAsync(
            state,
            context.Caller,
            cancellationToken).ConfigureAwait(false);
        if (admissionRejection is not null)
        {
            return RejectRead(admissionRejection);
        }

        try
        {
            if (state.IsRetired)
            {
                return RejectRead(WorkspaceOutcomeReasons.WorkspaceNotFound);
            }

            var authorizationRejection = GetDurableAccessRejection(
                state,
                context.Caller);
            if (authorizationRejection is not null)
            {
                return RejectRead(authorizationRejection);
            }

            lock (state.ContinuityGate)
            {
                if (!HasCurrentAttachmentUnderLock(state, context))
                {
                    return RejectRead(WorkspaceOutcomeReasons.StaleWorkspaceAttachment);
                }

                TouchWorkspace(state);
            }

            return query switch
            {
                ReadProjection projection => ReadWorkspaceProjection(state, projection),
                ReadCompilation compilation => ReadCompilationGeneration(
                    state,
                    compilation.CompilationGeneration),
                ReadTraceWindow trace => ReadTraceWindowCore(
                    state,
                    trace.Request,
                    cancellationToken),
                _ => RejectRead(WorkspaceOutcomeReasons.WorkspaceInternalDefect),
            };
        }
        finally
        {
            state.CommandGate.Release();
        }
    }

    private WorkspaceCommandOutcome Apply(
        WorkspaceState state,
        ApplyEdit command,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Reject(WorkspaceOutcomeReasons.WorkspaceCancelled);
        }

        if (AuthoringAdmission.RejectionForCommand(
                command.Intent,
                workspacePolicy) is { } commandPolicyEvidence)
        {
            return Reject(
                WorkspaceOutcomeReasons.WorkspaceAdmissionRejected,
                policyEvidence: commandPolicyEvidence);
        }

        var outcome = ProjectEditor.Apply(state.Revision, command.Intent);
        if (outcome is EditRejected rejected)
        {
            return Reject(
                rejected.Reason,
                rejected.Diagnostics.Select(item => item.Code));
        }

        var committed = (EditCommitted)outcome;
        if (AuthoringAdmission.RejectionForDocument(
                committed.Revision.Document,
                workspacePolicy) is { } documentPolicyEvidence)
        {
            return Reject(
                WorkspaceOutcomeReasons.WorkspaceAdmissionRejected,
                policyEvidence: documentPolicyEvidence);
        }

        if (ReferenceEquals(committed.Revision, state.Revision))
        {
            return new AuthoringCommitted(
                state.Revision.RevisionId,
                state.ProjectionVersion);
        }

        return CommitAuthoringRevision(state, committed.Revision);
    }

    private static WorkspaceProjection Project(WorkspaceState state)
    {
        return new WorkspaceProjection(
            state.Id,
            state.ProjectionVersion,
            state.Revision,
            state.Compilation,
            state.Simulation,
            new TransactionHistoryAvailability(
                state.HistoryCursor > 0,
                state.HistoryCursor < state.History.Count - 1,
                state.History.Count),
            ProjectDurability(state));
    }

    private static WorkspaceCommandRejected Reject(
        string code,
        IEnumerable<string>? diagnosticCodes = null,
        PolicyEvidenceProjection? policyEvidence = null)
    {
        return new WorkspaceCommandRejected(
            code,
            diagnosticCodes?.ToArray() ?? [],
            WorkspaceOutcomeReasons.RetryFor(code),
            policyEvidence);
    }

    private static WorkspaceReadOutcome ReadWorkspaceProjection(
        WorkspaceState state,
        ReadProjection query)
    {
        return query.AfterProjectionVersion == state.ProjectionVersion
            ? new ProjectionUnchanged(state.ProjectionVersion)
            : new ProjectionSnapshot(Project(state));
    }

    private static WorkspaceOpenRejected RejectOpen(
        string code,
        IEnumerable<string>? diagnosticCodes = null,
        PolicyEvidenceProjection? policyEvidence = null)
    {
        return new WorkspaceOpenRejected(
            code,
            diagnosticCodes?.ToArray() ?? [],
            WorkspaceOutcomeReasons.RetryFor(code),
            policyEvidence);
    }

    private static WorkspaceReadRejected RejectRead(string code)
    {
        return new WorkspaceReadRejected(
            code,
            [],
            WorkspaceOutcomeReasons.RetryFor(code));
    }

    private static AttachRejected RejectAttach(string code)
    {
        return new AttachRejected(code, [], WorkspaceOutcomeReasons.RetryFor(code));
    }

}
