using System.Diagnostics;
using LogicLab.Application.Workspaces;
using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.Presentation.Scene;
using LogicLab.Web.Components.Editor;
using LogicLab.Web.Transfers;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Localization;
using Microsoft.JSInterop;

namespace LogicLab.Web.Components.Pages;

public sealed partial class Editor : IAsyncDisposable
{
    private const ulong MaximumScenePortCount = 100_000;
    private static readonly TimeSpan CompilationRefreshInterval =
        TimeSpan.FromMilliseconds(250);
    private readonly IEditorWorkspace workspace;
    private readonly ProjectImportWorkflow projectImportWorkflow;
    private readonly TimeProvider timeProvider;
    private readonly CancellationTokenSource componentLifetime = new();
    private WorkspaceAttachmentNavigation? attachmentNavigation;
    private int isDisposed;

    public Editor(
        IEditorWorkspace workspace,
        ProjectImportWorkflow projectImportWorkflow,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(projectImportWorkflow);
        ArgumentNullException.ThrowIfNull(timeProvider);
        this.workspace = workspace;
        this.projectImportWorkflow = projectImportWorkflow;
        this.timeProvider = timeProvider;
    }

    private WorkspaceProjection? Projection { get; set; }

    private Attached? Attachment { get; set; }

    private WorkspaceAttachmentFailure? AttachmentFailure { get; set; }

    private AccessibleSceneProjection? Scene { get; set; }

    private CircuitDefinitionId? SelectedDefinitionId { get; set; }

    private List<HierarchyNavigationStep> HierarchyNavigation { get; } = [];

    private bool IsInteractive { get; set; }

    private bool StimulusIsScheduled { get; set; }

    private string? status;

    private string Status
    {
        get => status ?? Text["StatusConnecting"];
        set => status = value;
    }

    private string? ActiveCommand { get; set; }

    private string? PreparedExportUrl { get; set; }

    private string ClaimDisplayName { get; set; } = string.Empty;

    private WorkspaceCaller CurrentCaller { get; set; } =
        AnonymousWorkspaceCaller.Instance;

    private bool IsCallerAvailable { get; set; } = true;

    private string EditorPageTitle => AttachmentFailure?.Title ?? WorkbenchTitle;

    private string WorkbenchEyebrow => Projection?.Durability switch
    {
        DurableWorkspaceDurabilityProjection => Text["EyebrowDurable"],
        SandboxWorkspaceDurabilityProjection => Text["EyebrowSandbox"],
        _ when WorkspaceIdValue is not null => Text["EyebrowOpening"],
        _ => Text["EyebrowSandbox"],
    };

    private string WorkbenchTitle => Projection?.Durability switch
    {
        DurableWorkspaceDurabilityProjection => Text["TitleDurable"],
        _ when WorkspaceIdValue is not null && Projection is null => Text["TitleOpening"],
        _ => Text["TitleSandbox"],
    };

    private string WorkbenchDescription => Projection?.Durability switch
    {
        DurableWorkspaceDurabilityProjection => Text["DescriptionDurable"],
        _ when WorkspaceIdValue is not null && Projection is null => Text["DescriptionOpening"],
        _ => Text["DescriptionSandbox"],
    };

    [Parameter]
    public string? WorkspaceIdValue { get; set; }

    [CascadingParameter]
    private Task<AuthenticationState>? AuthenticationStateTask { get; set; }

    [Inject]
    private NavigationManager Navigation { get; set; } = null!;

    [Inject]
    private IJSRuntime JS { get; set; } = null!;

    [Inject]
    private IStringLocalizer<EditorText> Text { get; set; } = null!;

    protected override async Task OnParametersSetAsync()
    {
        var caller = AuthenticationStateTask is null
            ? AnonymousWorkspaceCaller.Instance
            : WorkspaceCallerAdapter.FromPrincipal(
                (await AuthenticationStateTask).User);
        if (caller is null)
        {
            if (!IsCallerAvailable)
            {
                return;
            }

            var invalidPriorCaller = CurrentCaller;
            var invalidAttachment = Attachment;
            IsCallerAvailable = false;
            ShowAttachmentFailure(WorkspaceOutcomeReasons.AuthenticationRequired);
            Status = Text["AuthenticationMissingSubject"];
            if (invalidAttachment is not null)
            {
                _ = await workspace.DetachAsync(
                    new DetachRequest(
                        invalidAttachment.Projection.WorkspaceId,
                        invalidAttachment.AttachmentId,
                        invalidAttachment.Generation,
                        invalidPriorCaller),
                    CancellationToken.None);
            }

            return;
        }

        if (IsCallerAvailable && caller == CurrentCaller)
        {
            return;
        }

        var priorCaller = CurrentCaller;
        CurrentCaller = caller;
        IsCallerAvailable = true;
        PreparedExportUrl = null;
        if (Attachment is not { } attachment)
        {
            return;
        }

        if (Projection?.Durability is SandboxWorkspaceDurabilityProjection)
        {
            return;
        }

        ShowAttachmentFailure("workspace_authorization_changed");
        Status = Text["AuthenticationChanged"];
        _ = await workspace.DetachAsync(
            new DetachRequest(
                attachment.Projection.WorkspaceId,
                attachment.AttachmentId,
                attachment.Generation,
                priorCaller),
            CancellationToken.None);
    }

    private bool CommandsAvailable => IsInteractive
        && IsCallerAvailable
        && (WorkspaceIdValue is null || Attachment is not null);

    private bool CanCreate => CommandsAvailable
        && WorkspaceIdValue is null
        && Projection is null;

    private bool CanAuthor => CommandsAvailable
        && Projection is not null
        && Projection.Simulation is null
        && Projection.Compilation is not CompilationPublishedProjection
        && Projection.ProjectRevision.Document.EntryCircuitDefinition.ComponentInstances.Count == 0;

    private bool CanPrepareExport => CommandsAvailable && Projection is not null;

    private bool ShowClaim => CommandsAvailable
        && CurrentCaller is AuthenticatedWorkspaceCaller
        && Projection?.Durability is SandboxWorkspaceDurabilityProjection;

    private bool CanClaim => ShowClaim && ClaimDisplayName.Length != 0;

    private bool ShowSave => CommandsAvailable
        && CurrentCaller is AuthenticatedWorkspaceCaller
        && Projection?.Durability is DurableWorkspaceDurabilityProjection;

    private bool CanSave => ShowSave
        && Projection?.Durability is DurableWorkspaceDurabilityProjection
        {
            SaveStatus: DurableSaveStatus.Changed,
        };

    private bool HasSaveConflict => ShowSave
        && Projection?.Durability is DurableWorkspaceDurabilityProjection
        {
            SaveStatus: DurableSaveStatus.Conflict,
        };

    private bool CanImport => CommandsAvailable;

    private bool CanAuthorHierarchy => CanAuthor;

    private bool CanAuthorSteering => CanAuthor;

    private bool CanAuthorArithmetic => CanAuthor;

    private bool CanSetEntryDefinition => CommandsAvailable
        && ActiveCommand is null
        && Projection?.Simulation is null;

    private bool CanEnterDefinitionInstances => Projection is not null
        && SelectedDefinitionId is not null
        && (HierarchyNavigation.Count != 0
            || SelectedDefinitionId == Projection.ProjectRevision.Document
                .EntryCircuitDefinitionId);

    private bool CanCompile => CommandsAvailable
        && Projection is not null
        && Projection.Simulation is null
        && Projection.Compilation is not CompilationPublishedProjection
        && Projection.ProjectRevision.Document.EntryCircuitDefinition.ComponentInstances.Count > 0;

    private bool CanCreateSession => CommandsAvailable
        && Projection?.Simulation is null
        && Projection?.Compilation is CompilationPublishedProjection;

    private bool HasProgrammableInputs => Projection?.ProjectRevision.Document
        .EntryCircuitDefinition.ComponentInstances.Any(IsProgrammableInput) is true;

    private bool CanScheduleStimulus => CommandsAvailable
        && Projection?.Simulation is not null
        && HasProgrammableInputs
        && !StimulusIsScheduled;

    private bool CanStep => CommandsAvailable
        && Projection?.Simulation is not null
        && StimulusIsScheduled;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender && RendererInfo.IsInteractive)
        {
            attachmentNavigation = new WorkspaceAttachmentNavigation(JS);
            IsInteractive = true;
            if (!IsCallerAvailable)
            {
                StateHasChanged();
                return;
            }

            if (WorkspaceIdValue is null)
            {
                Status = Text["StatusReady"];
            }
            else
            {
                await AttachOpenedWorkspaceAsync();
            }

            StateHasChanged();
        }
    }

    private async Task AttachOpenedWorkspaceAsync()
    {
        var caller = RequireCurrentCaller();
        var workspaceId = new LogicLab.Application.Workspaces.WorkspaceId(
            WorkspaceIdValue!);
        var workspaceLocator = CreateWorkspaceLocator(workspaceId);
        var browserHistoryEntryState = await (attachmentNavigation
                ?? throw new InvalidOperationException(
                    "Attachment navigation is unavailable before interactive rendering."))
            .ReadHistoryEntryStateAsync(workspaceLocator, componentLifetime.Token);
        var hasPriorFence = WorkspaceAttachmentHistoryState.TryRead(
            browserHistoryEntryState ?? Navigation.HistoryEntryState,
            workspaceId,
            out var priorAttachmentId,
            out var priorGeneration);
        var attachOutcome = await workspace.AttachAsync(
            hasPriorFence
                ? new Reattach(
                    workspaceId,
                    priorAttachmentId!,
                    priorGeneration,
                    LogicLabWebBuild.Fingerprint,
                    caller)
                : new InitialAttach(
                    workspaceId,
                    LogicLabWebBuild.Fingerprint,
                    caller),
            componentLifetime.Token);
        if (!hasPriorFence
            && attachOutcome is AttachRejected
            {
                Code: "stale_workspace_attachment",
                RetryDisposition: RetryDisposition.Reattach,
            }
            && caller is AuthenticatedWorkspaceCaller authenticatedCaller
            && IsCallerAvailable
            && caller == CurrentCaller)
        {
            attachOutcome = await workspace.AttachAsync(
                new RecoverAttach(
                    new LogicLab.Application.Workspaces.WorkspaceId(WorkspaceIdValue!),
                    LogicLabWebBuild.Fingerprint,
                    authenticatedCaller),
                componentLifetime.Token);
        }

        if (attachOutcome is Attached attached)
        {
            if (!await CanPublishAttachmentAsync(attached, caller))
            {
                return;
            }

            Attachment = attached;
            AttachmentFailure = null;
            Projection = attached.Projection;
            ClaimDisplayName = attached.Projection.ProjectRevision.Document.DisplayName;
            SelectedDefinitionId = attached.Projection.ProjectRevision.Document
                .EntryCircuitDefinitionId;
            HierarchyNavigation.Clear();
            ProjectScene();
            await PreserveAttachmentFenceAsync(attached);
            Status = attached.Projection.Durability
                is DurableWorkspaceDurabilityProjection
                    ? Text["StatusReopenedDurable"]
                    : Text["StatusReopenedSandbox"];
            return;
        }

        if (!IsCallerAvailable || caller != CurrentCaller)
        {
            ShowAttachmentFailure("workspace_authorization_changed");
            Status = Text["AuthenticationChanged"];
            return;
        }

        var rejectionCode = attachOutcome switch
        {
            AttachRejected rejected => rejected.Code,
            Expired expired => expired.Code,
            _ => throw new UnreachableException(),
        };
        ShowAttachmentFailure(rejectionCode);
        Status = Text["AttachmentRejected", rejectionCode];
    }

    private async Task RunCommandAsync(
        string command,
        Func<bool> canExecute,
        Func<Task> operation)
    {
        ArgumentException.ThrowIfNullOrEmpty(command);
        ArgumentNullException.ThrowIfNull(canExecute);
        ArgumentNullException.ThrowIfNull(operation);
        if (ActiveCommand is not null || !canExecute())
        {
            return;
        }

        ActiveCommand = command;
        try
        {
            await operation();
        }
        finally
        {
            ActiveCommand = null;
        }
    }

    private async Task CreateProject()
    {
        var caller = RequireCurrentCaller();
        var outcome = await workspace.OpenAsync(
            new CreateSandbox("Sandbox Project", "Main", caller),
            CancellationToken.None);
        if (outcome is not WorkspaceOpened opened)
        {
            Status = Text[
                "ProjectCreationRejected",
                ((WorkspaceOpenRejected)outcome).Code];
            return;
        }

        var attachOutcome = await workspace.AttachAsync(
            new InitialAttach(
                opened.WorkspaceId,
                LogicLabWebBuild.Fingerprint,
                caller),
            CancellationToken.None);
        if (attachOutcome is not Attached attached)
        {
            var code = attachOutcome switch
            {
                AttachRejected rejected => rejected.Code,
                Expired expired => expired.Code,
                _ => throw new UnreachableException(),
            };
            Status = Text["AttachmentRejected", code];
            return;
        }

        if (!await CanPublishAttachmentAsync(attached, caller))
        {
            return;
        }

        Attachment = attached;
        AttachmentFailure = null;
        Projection = attached.Projection;
        ClaimDisplayName = attached.Projection.ProjectRevision.Document.DisplayName;
        SelectedDefinitionId = attached.Projection.ProjectRevision.Document
            .EntryCircuitDefinitionId;
        HierarchyNavigation.Clear();
        ProjectScene();
        await PreserveAttachmentFenceAsync(attached);
        Status = Text["StatusSandboxCreated"];
    }

    private void UpdateClaimDisplayName(string value)
    {
        ClaimDisplayName = value;
    }

    private async Task ClaimSandboxProject()
    {
        var projection = Projection
            ?? throw new InvalidOperationException("A Workspace is not attached.");
        var outcome = await Execute(context => new ClaimSandbox(
            context,
            new ClaimPrecondition(projection.ProjectRevision.RevisionId),
            ClaimDisplayName));
        Status = outcome switch
        {
            DurableProjectClaimed claimed => Text[
                "ClaimSucceeded",
                claimed.DisplayName.Value],
            WorkspaceCommandRejected rejected => Text["ClaimRejected", rejected.Code],
            _ => throw new UnreachableException(),
        };
    }

    private async Task SaveDurableProject()
    {
        var projection = Projection
            ?? throw new InvalidOperationException("A Workspace is not attached.");
        var durability = projection.Durability as DurableWorkspaceDurabilityProjection
            ?? throw new InvalidOperationException("The Workspace is not durable.");
        var outcome = await Execute(context => new SaveDurable(
            context,
            new DurableSavePrecondition(
                projection.ProjectRevision.RevisionId,
                durability.ObservedDurableVersion)));
        Status = outcome switch
        {
            DurableProjectSaved saved => Text[
                "SaveSucceeded",
                saved.DurableVersion.Value],
            DurableProjectSaveConflict => Text["SaveConflictStatus"],
            WorkspaceCommandRejected rejected => Text["SaveRejected", rejected.Code],
            _ => throw new UnreachableException(),
        };
    }

    private async Task ReloadDurableProject()
    {
        var durability = Projection?.Durability as DurableWorkspaceDurabilityProjection
            ?? throw new InvalidOperationException("The Workspace is not durable.");
        await OpenIndependentWorkspace(
            new OpenDurable(durability.DurableProjectId, RequireCurrentCaller()),
            Text["OpeningLatestDurable"]);
    }

    private async Task KeepConflictAsCopy()
    {
        var attachment = Attachment
            ?? throw new InvalidOperationException("A Workspace is not attached.");
        var projection = Projection
            ?? throw new InvalidOperationException("A Workspace is not attached.");
        await OpenIndependentWorkspace(
            new CopyWorkspace(
                projection.WorkspaceId,
                attachment.AttachmentId,
                attachment.Generation,
                projection.ProjectionVersion,
                WorkspaceCopySaveTarget.DetachedSandbox,
                RequireCurrentCaller()),
            Text["OpeningCopy"]);
    }

    private async Task OpenIndependentWorkspace(
        OpenWorkspaceRequest request,
        string openingStatus)
    {
        Status = openingStatus;
        var outcome = await workspace.OpenAsync(request, componentLifetime.Token);
        if (outcome is WorkspaceOpenRejected rejected)
        {
            Status = Text["OpeningRejected", rejected.Code];
            return;
        }

        var opened = (WorkspaceOpened)outcome;
        Navigation.NavigateTo(
            CreateWorkspaceLocator(opened.WorkspaceId),
            new NavigationOptions
            {
                ForceLoad = true,
                ReplaceHistoryEntry = true,
            });
    }

    private async Task AuthorCircuit()
    {
        if (Projection is null)
        {
            return;
        }

        var definitionId = Projection.ProjectRevision.Document.EntryCircuitDefinitionId;
        if (!await Apply(new PlaceComponentInstanceIntent(
                definitionId,
                Contract("source.input"),
                [
                    new ComponentParameterBinding("width", new Unsigned32ParameterValue(1)),
                    new ComponentParameterBinding(
                        "initialValue",
                        new LogicVectorParameterValue([LogicValue.Zero])),
                ],
                new ComponentPlacement(new GridPoint(0, 0)),
                "Input")))
        {
            return;
        }

        var input = Find("source.input");
        if (!await Apply(new PlaceComponentInstanceIntent(
                definitionId,
                Contract("logic.not"),
                [new ComponentParameterBinding("width", new Unsigned32ParameterValue(1))],
                new ComponentPlacement(new GridPoint(4, 0)),
                "NOT")))
        {
            return;
        }

        var logicNot = Find("logic.not");
        if (!await Apply(new PlaceComponentInstanceIntent(
                definitionId,
                Contract("sink.output"),
                [
                    new ComponentParameterBinding("width", new Unsigned32ParameterValue(1)),
                    new ComponentParameterBinding("radix", new ChoiceParameterValue("binary")),
                ],
                new ComponentPlacement(new GridPoint(8, 0)),
                "Output")))
        {
            return;
        }

        var output = Find("sink.output");
        if (!await Apply(new ConnectTerminalsIntent([
                Terminal(definitionId, input.Id, "Q"),
                Terminal(definitionId, logicNot.Id, "A"),
            ]))
            || !await Apply(new ConnectTerminalsIntent([
                Terminal(definitionId, logicNot.Id, "Q"),
                Terminal(definitionId, output.Id, "D"),
            ])))
        {
            return;
        }

        Status = Text["CircuitAuthored"];
    }

    private async Task Compile()
    {
        var projection = Projection!;
        var revision = projection.ProjectRevision;
        var precondition = new CompilationPrecondition(
            revision.RevisionId,
            revision.Document.EntryCircuitDefinitionId,
            revision.Document.LibrarySnapshot.Fingerprint);
        var observationCancellationToken = componentLifetime.Token;
        WorkspaceCommandOutcome outcome;
        try
        {
            outcome = await Execute(
                context => new RequestCompilation(context, precondition),
                commandCancellationToken: CancellationToken.None,
                observationCancellationToken: observationCancellationToken);
        }
        catch (OperationCanceledException)
            when (observationCancellationToken.IsCancellationRequested)
        {
            return;
        }

        if (outcome is not CompilationAccepted accepted)
        {
            Status = Text[
                "CompilationRejectedStatus",
                ((WorkspaceCommandRejected)outcome).Code];
            return;
        }

        Status = Text[
            "CompilationAccepted",
            accepted.CompilationGeneration.Value];
        WorkspaceReadOutcome? observation;
        try
        {
            observation = await WaitForCompilationAsync(
                accepted.CompilationGeneration,
                observationCancellationToken);
        }
        catch (OperationCanceledException)
            when (observationCancellationToken.IsCancellationRequested)
        {
            return;
        }

        if (observation is null)
        {
            Status = Text["CompilationStatusDetached"];
            return;
        }

        Status = observation switch
        {
            CompilationSnapshot { Compilation: CompilationPublishedProjection } =>
                Text["CompilationArtifactPublished"],
            CompilationSnapshot { Compilation: CompilationSupersededProjection } => Text[
                "CompilationWasSuperseded",
                accepted.CompilationGeneration.Value],
            CompilationSnapshot { Compilation: CompilationRejectedProjection rejected } =>
                Text["CompilationRejectedStatus", rejected.RejectionCode],
            WorkspaceReadRejected rejected =>
                Text["CompilationStatusUnavailable", rejected.Code],
            _ => Text["CompilationEndedUnknown"],
        };
    }

    private async Task<WorkspaceReadOutcome?> WaitForCompilationAsync(
        CompilationGeneration generation,
        CancellationToken cancellationToken)
    {
        var reattachAttempted = false;
        while (Projection is not null)
        {
            var read = await workspace.ReadAsync(
                QueryContext(),
                new ReadCompilation(generation),
                cancellationToken);
            if (read is WorkspaceReadRejected
                {
                    RetryDisposition: RetryDisposition.Reattach,
                }
                && !reattachAttempted
                && await TryReattachAsync(cancellationToken))
            {
                reattachAttempted = true;
                continue;
            }

            if (read is not CompilationSnapshot snapshot)
            {
                await Refresh(cancellationToken);
                return read;
            }

            if (snapshot.Compilation is CompilationQueuedProjection
                or CompilationRunningProjection)
            {
                await Task.Delay(
                    CompilationRefreshInterval,
                    timeProvider,
                    cancellationToken);
                await Refresh(cancellationToken);
                continue;
            }

            await Refresh(cancellationToken);
            return snapshot;
        }

        return null;
    }

    private async Task PrepareProjectExport()
    {
        var revision = Projection?.ProjectRevision
            ?? throw new InvalidOperationException("Workspace is not open.");
        var revisionId = revision.RevisionId;
        var outcome = await Execute(context => new PrepareExport(
            context,
            new AuthoringPrecondition(revisionId),
            revisionId));
        if (outcome is not ExportPrepared prepared)
        {
            PreparedExportUrl = null;
            Status = Text[
                "ExportRejected",
                ((WorkspaceCommandRejected)outcome).Code];
            return;
        }

        PreparedExportUrl =
            $"/downloads/{Uri.EscapeDataString(prepared.ExportTicket.Value)}";
        Status = Text["ExportPrepared", prepared.ExpiresAfterSeconds];
    }

    private async Task ImportProjectPackage(InputFileChangeEventArgs change)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (ActiveCommand is not null || !CanImport)
        {
            return;
        }

        ActiveCommand = "import";
        PreparedExportUrl = null;
        try
        {
            await using var source = change.File.OpenReadStream(
                projectImportWorkflow.MaximumCarrierBytes,
                componentLifetime.Token);
            var outcome = await projectImportWorkflow.ImportAsync(
                source,
                RequireCurrentCaller(),
                componentLifetime.Token);
            if (outcome is WorkspaceOpenRejected rejected)
            {
                Status = Text["ImportRejected", rejected.Code];
                return;
            }

            var imported = (WorkspaceOpened)outcome;
            Status = Text["ImportOpening"];
            Navigation.NavigateTo(
                CreateWorkspaceLocator(imported.WorkspaceId),
                new NavigationOptions
                {
                    ForceLoad = true,
                    ReplaceHistoryEntry = true,
                });
        }
        catch (OperationCanceledException)
            when (componentLifetime.IsCancellationRequested)
        {
            Status = Text["ImportCancelled"];
        }
        catch (IOException)
        {
            Status = Text["ImportRejected", "package_limit_exceeded"];
        }
        finally
        {
            ActiveCommand = null;
        }
    }

    private async Task CreateSimulationSession()
    {
        var compilation = Projection?.Compilation as CompilationPublishedProjection
            ?? throw new InvalidOperationException("Compilation is not published.");
        var precondition = new SessionCreationPrecondition(
            compilation.ArtifactKey);
        var outcome = await Execute(context => new CreateSession(context, precondition));
        if (outcome is not SimulationSessionCreated)
        {
            Status = Text[
                "SessionRejected",
                ((WorkspaceCommandRejected)outcome).Code];
            return;
        }

        Status = HasProgrammableInputs
            ? Text["SessionCreated"]
            : Text["SessionCreatedNoInputs"];
    }

    private async Task ScheduleStimulus()
    {
        var assignments = Projection!.ProjectRevision.Document.EntryCircuitDefinition
            .ComponentInstances
            .Where(IsProgrammableInput)
            .Select(CreateHighStimulus)
            .ToArray();
        var logicalTime = checked(Projection!.Simulation!.LogicalTime + 1);
        var precondition = SessionPrecondition();
        var outcome = await Execute(context => new ScheduleInputStimulus(
            context,
            precondition,
            logicalTime,
            assignments));
        StimulusIsScheduled = outcome is StimulusScheduled;
        Status = StimulusIsScheduled
            ? Text["StimulusScheduled", logicalTime]
            : Text["StimulusRejected", ((WorkspaceCommandRejected)outcome).Code];
    }

    private async Task Step()
    {
        var precondition = SessionPrecondition();
        var outcome = await Execute(context => new StepSession(context, precondition));
        StimulusIsScheduled = false;
        Status = outcome switch
        {
            SessionStepped stepped => Text["StepCommitted", stepped.LogicalTime],
            SessionAdvanceFailed failed => Text[
                "StepFailed",
                AdvanceFailureText(failed.Failure.Reason)],
            WorkspaceCommandRejected rejected => Text["StepRejected", rejected.Code],
            _ => Text["StepFailed", Text["FailureSimulationInternal"]],
        };
    }

    private string AdvanceFailureText(AdvanceFailureReason reason)
    {
        return reason switch
        {
            AdvanceFailureReason.ZeroTimeOscillation => Text["FailureZeroTimeOscillation"],
            AdvanceFailureReason.SimulationResourceLimit =>
                Text["FailureSimulationResourceLimit"],
            AdvanceFailureReason.SimulationCancelled =>
                Text["FailureSimulationCancelled"],
            AdvanceFailureReason.SimulationInfrastructureFailure =>
                Text["FailureSimulationInfrastructure"],
            AdvanceFailureReason.SimulationInternalDefect =>
                Text["FailureSimulationInternal"],
            _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null),
        };
    }

    private async Task<bool> Apply(EditIntent intent)
    {
        ArgumentNullException.ThrowIfNull(intent);
        var projection = Projection;
        if (projection is null)
        {
            return false;
        }

        var precondition = new AuthoringPrecondition(
            projection.ProjectRevision.RevisionId);
        var outcome = await Execute(context => new ApplyEdit(
            context,
            precondition,
            intent));
        if (outcome is AuthoringCommitted)
        {
            return Projection is not null;
        }

        if (Projection is null)
        {
            return false;
        }

        Status = Text["AuthoringRejected", ((WorkspaceCommandRejected)outcome).Code];
        return false;
    }

    private async Task<WorkspaceCommandOutcome> Execute(
        Func<WorkspaceCommandContext, WorkspaceCommand> createCommand,
        CancellationToken commandCancellationToken = default,
        CancellationToken observationCancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(createCommand);
        var outcome = await workspace.DispatchAsync(
            createCommand(CommandContext(CreateClientIntentId())),
            commandCancellationToken);
        if (outcome is WorkspaceCommandRejected
            {
                RetryDisposition: RetryDisposition.Reattach,
            }
            && await TryReattachAsync(observationCancellationToken))
        {
            outcome = await workspace.DispatchAsync(
                createCommand(CommandContext(CreateClientIntentId())),
                commandCancellationToken);
        }

        await Refresh(observationCancellationToken);
        return outcome;
    }

    private async Task<bool> TryReattachAsync(CancellationToken cancellationToken)
    {
        if (Attachment is not { } attachment || Projection is not { } projection)
        {
            return false;
        }

        var caller = RequireCurrentCaller();
        var outcome = await workspace.AttachAsync(
            new Reattach(
                projection.WorkspaceId,
                attachment.AttachmentId,
                attachment.Generation,
                LogicLabWebBuild.Fingerprint,
                caller),
            cancellationToken);
        if (outcome is not Attached reattached)
        {
            return false;
        }

        if (!await CanPublishAttachmentAsync(
                reattached,
                caller,
                expectedCurrentAttachment: attachment))
        {
            return false;
        }

        Attachment = reattached;
        UpdateProjection(reattached.Projection);
        await PreserveAttachmentFenceAsync(reattached);
        return true;
    }

    private async Task PreserveAttachmentFenceAsync(Attached attachment)
    {
        var navigation = attachmentNavigation
            ?? throw new InvalidOperationException(
                "Attachment navigation is unavailable before interactive rendering.");
        await navigation.ReplaceHistoryEntryAsync(
            CreateWorkspaceLocator(attachment.Projection.WorkspaceId),
            WorkspaceAttachmentHistoryState.Serialize(attachment),
            componentLifetime.Token);
    }

    private static string CreateWorkspaceLocator(WorkspaceId workspaceId)
    {
        return $"/editor/{Uri.EscapeDataString(workspaceId.Value)}";
    }

    private async Task<bool> CanPublishAttachmentAsync(
        Attached attached,
        WorkspaceCaller caller,
        Attached? expectedCurrentAttachment = null)
    {
        var attachmentWasSuperseded = expectedCurrentAttachment is not null
            && !HasCurrentFence(expectedCurrentAttachment);
        var authorizationChanged = !IsCallerAvailable
            || (attached.Projection.Durability
                    is not SandboxWorkspaceDurabilityProjection
                && caller != CurrentCaller);
        if (Volatile.Read(ref isDisposed) == 0
            && !attachmentWasSuperseded
            && !authorizationChanged)
        {
            return true;
        }

        if (authorizationChanged)
        {
            var code = IsCallerAvailable
                ? "workspace_authorization_changed"
                : WorkspaceOutcomeReasons.AuthenticationRequired;
            ShowAttachmentFailure(code);
            Status = IsCallerAvailable
                ? Text["AuthenticationChanged"]
                : Text["AuthenticationMissingSubject"];
        }

        _ = await workspace.DetachAsync(
            new DetachRequest(
                attached.Projection.WorkspaceId,
                attached.AttachmentId,
                attached.Generation,
                caller),
            CancellationToken.None);
        return false;
    }

    private bool HasCurrentFence(Attached expected)
    {
        return Attachment is { } current
            && current.Projection.WorkspaceId == expected.Projection.WorkspaceId
            && current.AttachmentId == expected.AttachmentId
            && current.Generation == expected.Generation;
    }

    private async Task Refresh(CancellationToken cancellationToken)
    {
        if (Projection is null || Attachment is not { } attachment)
        {
            return;
        }

        var caller = RequireCurrentCaller();
        var read = await workspace.ReadAsync(
            QueryContext(),
            ReadProjection.Instance,
            cancellationToken);
        if (!IsCallerAvailable
            || caller != CurrentCaller
            || Attachment is not { } currentAttachment
            || currentAttachment.AttachmentId != attachment.AttachmentId
            || currentAttachment.Generation != attachment.Generation)
        {
            return;
        }

        if (read is ProjectionSnapshot snapshot)
        {
            UpdateProjection(snapshot.Projection);
            return;
        }

        var rejectionCode = ((WorkspaceReadRejected)read).Code;
        if (WorkspaceIdValue is not null
            || Projection.Durability is not SandboxWorkspaceDurabilityProjection)
        {
            ShowAttachmentFailure(rejectionCode);
            return;
        }

        ClearWorkspaceState();
        Status = Text["WorkspaceClosed", rejectionCode];
    }

    private void ClearWorkspaceState()
    {
        Projection = null;
        Attachment = null;
        Scene = null;
        SelectedDefinitionId = null;
        HierarchyNavigation.Clear();
        StimulusIsScheduled = false;
        RouteDraftActive = false;
        PreparedExportUrl = null;
    }

    private void ShowAttachmentFailure(string code)
    {
        ClearWorkspaceState();
        AttachmentFailure = WorkspaceAttachmentFailure.From(code, Text);
    }

    private void ReloadApplication()
    {
        Navigation.Refresh(forceReload: true);
    }

    private void UpdateProjection(WorkspaceProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        var projectRevisionChanged = Projection?.ProjectRevision.RevisionId
            != projection.ProjectRevision.RevisionId;
        Projection = projection;
        if (projectRevisionChanged)
        {
            PreparedExportUrl = null;
            ProjectScene();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref isDisposed, 1) != 0)
        {
            return;
        }

        var attachment = Attachment;
        await componentLifetime.CancelAsync();
        try
        {
            if (attachment is not null)
            {
                _ = await workspace.DetachAsync(
                    new DetachRequest(
                        attachment.Projection.WorkspaceId,
                        attachment.AttachmentId,
                        attachment.Generation,
                        CurrentCaller),
                    CancellationToken.None);
            }
        }
        finally
        {
            try
            {
                if (attachmentNavigation is not null)
                {
                    await attachmentNavigation.DisposeAsync();
                }
            }
            finally
            {
                componentLifetime.Dispose();
            }
        }
    }

    private static ClientIntentId CreateClientIntentId()
    {
        return new ClientIntentId(Guid.CreateVersion7().ToString("N"));
    }

    private WorkspaceCommandContext CommandContext(ClientIntentId clientIntentId)
    {
        ArgumentNullException.ThrowIfNull(clientIntentId);
        var projection = Projection
            ?? throw new InvalidOperationException("Workspace is not open.");
        var attachment = Attachment
            ?? throw new InvalidOperationException("Workspace is not attached.");
        return new WorkspaceCommandContext(
            projection.WorkspaceId,
            attachment.AttachmentId,
            attachment.Generation,
            clientIntentId,
            RequireCurrentCaller());
    }

    private WorkspaceQueryContext QueryContext()
    {
        var projection = Projection
            ?? throw new InvalidOperationException("Workspace is not open.");
        var attachment = Attachment
            ?? throw new InvalidOperationException("Workspace is not attached.");
        return new WorkspaceQueryContext(
            projection.WorkspaceId,
            attachment.AttachmentId,
            attachment.Generation,
            RequireCurrentCaller());
    }

    private WorkspaceCaller RequireCurrentCaller()
    {
        return IsCallerAvailable
            ? CurrentCaller
            : throw new InvalidOperationException(
                "The current authentication state has no stable Workspace caller.");
    }

    private SessionMutationPrecondition SessionPrecondition()
    {
        var projection = Projection
            ?? throw new InvalidOperationException("Workspace is not open.");
        var simulation = projection.Simulation
            ?? throw new InvalidOperationException("Simulation Session is not open.");
        return new SessionMutationPrecondition(
            simulation.SessionId,
            simulation.SessionVersion,
            simulation.CompilationArtifactKey);
    }

    private void ProjectScene()
    {
        if (Projection is null)
        {
            Scene = null;
            return;
        }

        var document = Projection.ProjectRevision.Document;
        if (SelectedDefinitionId is null
            || document.FindCircuitDefinition(SelectedDefinitionId) is null)
        {
            SelectedDefinitionId = document.EntryCircuitDefinitionId;
            HierarchyNavigation.Clear();
        }

        if (AccessibleSceneProjector.TryProject(
                Projection.ProjectRevision,
                SelectedDefinitionId,
                MaximumScenePortCount,
                out var scene))
        {
            Scene = scene;
            return;
        }

        Scene = null;
        Status = Text["ScenePolicyExceeded"];
    }

    private ComponentInstance Find(string contractId)
    {
        var key = Contract(contractId);
        return Projection!.ProjectRevision.Document.EntryCircuitDefinition.ComponentInstances
            .Single(instance => instance.Target is LibraryComponentTarget library
                && library.ContractKey == key);
    }

    private static bool IsProgrammableInput(ComponentInstance instance)
    {
        return instance.Target is LibraryComponentTarget library
            && string.Equals(
                library.ContractKey.LibraryId,
                CoreLibrarySchema.LibraryId,
                StringComparison.Ordinal)
            && string.Equals(
                library.ContractKey.ContractId,
                "source.input",
                StringComparison.Ordinal);
    }

    private static InputStimulusAssignment CreateHighStimulus(ComponentInstance input)
    {
        var width = input.Parameters.Single(parameter => string.Equals(
            parameter.ParameterId,
            "width",
            StringComparison.Ordinal)).Value as Unsigned32ParameterValue
            ?? throw new InvalidOperationException(
                "A programmable input must define its validated width.");
        return new InputStimulusAssignment(
            input.Id,
            [.. Enumerable.Repeat(LogicValue.One, checked((int)width.Value))]);
    }

    private static ComponentContractKey Contract(string contractId)
    {
        return new ComponentContractKey(CoreLibrarySchema.LibraryId, contractId);
    }

    private static InstanceTerminalReference Terminal(
        CircuitDefinitionId definitionId,
        ComponentInstanceId componentId,
        string portId)
    {
        return new InstanceTerminalReference(definitionId, componentId, portId);
    }

    private sealed record WorkspaceAttachmentFailure(
        string Code,
        string Title,
        string Description)
    {
        public static WorkspaceAttachmentFailure From(
            string code,
            IStringLocalizer<EditorText> text)
        {
            ArgumentException.ThrowIfNullOrEmpty(code);
            ArgumentNullException.ThrowIfNull(text);
            return code switch
            {
                WorkspaceOutcomeReasons.WorkspaceNotFound
                    or WorkspaceOutcomeReasons.WorkspaceExpired => new(
                    code,
                    text["FailureUnavailableTitle"],
                    text["FailureUnavailableDescription"]),
                "workspace_authorization_failed" or "workspace_authorization_changed" => new(
                    code,
                    text["FailureAccessChangedTitle"],
                    text["FailureAccessChangedDescription"]),
                WorkspaceOutcomeReasons.StaleWorkspaceAttachment => new(
                    code,
                    text["FailureStaleTitle"],
                    text["FailureStaleDescription"]),
                WorkspaceOutcomeReasons.BuildFingerprintMismatch => new(
                    code,
                    text["FailureUpdatedTitle"],
                    text["FailureUpdatedDescription"]),
                _ => new(
                    code,
                    text["FailureGenericTitle"],
                    text["FailureGenericDescription"]),
            };
        }
    }
}
