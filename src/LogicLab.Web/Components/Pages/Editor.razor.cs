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

namespace LogicLab.Web.Components.Pages;

public sealed partial class Editor : IAsyncDisposable
{
    private const ulong MaximumScenePortCount = 100_000;
    private static readonly TimeSpan CompilationRefreshInterval =
        TimeSpan.FromMilliseconds(250);
    private readonly IEditorWorkspace workspace;
    private readonly TimeProvider timeProvider;
    private readonly CancellationTokenSource componentLifetime = new();
    private int isDisposed;

    public Editor(IEditorWorkspace workspace, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(timeProvider);
        this.workspace = workspace;
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

    private string Status { get; set; } = "Connecting to the interactive workbench…";

    private string? ActiveCommand { get; set; }

    private string? PreparedExportUrl { get; set; }

    private WorkspaceCaller CurrentCaller { get; set; } =
        AnonymousWorkspaceCaller.Instance;

    private string EditorPageTitle => AttachmentFailure?.Title ?? WorkbenchTitle;

    private string WorkbenchEyebrow => Projection?.Durability switch
    {
        DurableWorkspaceDurabilityProjection => "Durable circuit workspace",
        SandboxWorkspaceDurabilityProjection => "Interactive circuit sandbox",
        _ when WorkspaceIdValue is not null => "Opening circuit workspace",
        _ => "Interactive circuit sandbox",
    };

    private string WorkbenchTitle => Projection?.Durability switch
    {
        DurableWorkspaceDurabilityProjection => "Durable Project Workbench",
        _ when WorkspaceIdValue is not null && Projection is null => "Workspace Workbench",
        _ => "Sandbox Workbench",
    };

    private string WorkbenchDescription => Projection?.Durability switch
    {
        DurableWorkspaceDurabilityProjection =>
            "Inspect the saved revision, compile it, and continue in an authorized workspace.",
        _ when WorkspaceIdValue is not null && Projection is null =>
            "Re-establishing the attachment fence before any project data or commands are shown.",
        _ =>
            "Create Circuit Definitions, navigate occurrences, choose an entry, compile, and simulate.",
    };

    [Parameter]
    public string? WorkspaceIdValue { get; set; }

    [CascadingParameter]
    private Task<AuthenticationState>? AuthenticationStateTask { get; set; }

    [Inject]
    private NavigationManager Navigation { get; set; } = null!;

    protected override async Task OnParametersSetAsync()
    {
        var caller = AuthenticationStateTask is null
            ? AnonymousWorkspaceCaller.Instance
            : WorkspaceCallerAdapter.FromPrincipal(
                (await AuthenticationStateTask).User);
        if (caller == CurrentCaller)
        {
            return;
        }

        var priorCaller = CurrentCaller;
        CurrentCaller = caller;
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
        Status = "Authentication changed. Reload the Workspace to continue.";
        _ = await workspace.DetachAsync(
            new DetachRequest(
                attachment.Projection.WorkspaceId,
                attachment.AttachmentId,
                attachment.Generation,
                priorCaller),
            CancellationToken.None);
    }

    private bool CommandsAvailable => IsInteractive
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
            IsInteractive = true;
            if (WorkspaceIdValue is null)
            {
                Status = "Ready to create a Sandbox Project.";
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
        var caller = CurrentCaller;
        var attachOutcome = await workspace.AttachAsync(
            new InitialAttach(
                new LogicLab.Application.Workspaces.WorkspaceId(WorkspaceIdValue!),
                LogicLabWebBuild.Fingerprint,
                caller),
            componentLifetime.Token);
        if (attachOutcome is AttachRejected
            {
                Code: "stale_workspace_attachment",
                RetryDisposition: RetryDisposition.Reattach,
            }
            && caller is AuthenticatedWorkspaceCaller authenticatedCaller
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
            SelectedDefinitionId = attached.Projection.ProjectRevision.Document
                .EntryCircuitDefinitionId;
            HierarchyNavigation.Clear();
            ProjectScene();
            Status = attached.Projection.Durability
                is DurableWorkspaceDurabilityProjection
                    ? "Durable Project reopened."
                    : "Sandbox Workspace reopened.";
            return;
        }

        if (caller != CurrentCaller)
        {
            ShowAttachmentFailure("workspace_authorization_changed");
            Status = "Authentication changed. Reload the Workspace to continue.";
            return;
        }

        var rejectionCode = attachOutcome switch
        {
            AttachRejected rejected => rejected.Code,
            Expired expired => expired.Code,
            _ => throw new UnreachableException(),
        };
        ShowAttachmentFailure(rejectionCode);
        Status = $"Workspace attachment rejected: {rejectionCode}.";
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
        var outcome = await workspace.OpenAsync(
            new CreateSandbox("Sandbox Project", "Main"),
            CancellationToken.None);
        if (outcome is not WorkspaceOpened opened)
        {
            Status = $"Project creation rejected: {((WorkspaceOpenRejected)outcome).Code}.";
            return;
        }

        var caller = CurrentCaller;
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
            Status = $"Workspace attachment rejected: {code}.";
            return;
        }

        if (!await CanPublishAttachmentAsync(attached, caller))
        {
            return;
        }

        Attachment = attached;
        AttachmentFailure = null;
        Projection = attached.Projection;
        SelectedDefinitionId = attached.Projection.ProjectRevision.Document
            .EntryCircuitDefinitionId;
        HierarchyNavigation.Clear();
        ProjectScene();
        Status = "Sandbox Project created. Author the sample circuit.";
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

        Status = "Circuit authored. Compile the current Project Revision.";
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
            Status = $"Compilation rejected: {((WorkspaceCommandRejected)outcome).Code}.";
            return;
        }

        Status = $"Compilation generation {accepted.CompilationGeneration.Value} accepted.";
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
            Status = "Compilation status is unavailable because the Workspace detached.";
            return;
        }

        Status = observation switch
        {
            CompilationSnapshot { Compilation: CompilationPublishedProjection } =>
                "Compilation Artifact published atomically.",
            CompilationSnapshot { Compilation: CompilationSupersededProjection } =>
                $"Compilation generation {accepted.CompilationGeneration.Value} was superseded.",
            CompilationSnapshot { Compilation: CompilationRejectedProjection rejected } =>
                $"Compilation rejected: {rejected.RejectionCode}.",
            WorkspaceReadRejected rejected =>
                $"Compilation status unavailable: {rejected.Code}.",
            _ => "Compilation ended in an unknown state.",
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
            Status = $"Export preparation rejected: {((WorkspaceCommandRejected)outcome).Code}.";
            return;
        }

        PreparedExportUrl =
            $"/downloads/{Uri.EscapeDataString(prepared.ExportTicket.Value)}";
        Status = $"Export prepared for {prepared.ExpiresAfterSeconds} seconds.";
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
            Status = $"Session creation rejected: {((WorkspaceCommandRejected)outcome).Code}.";
            return;
        }

        Status = HasProgrammableInputs
            ? "Simulation Session created at Logical Time 0."
            : "Simulation Session created at Logical Time 0. "
                + "This circuit has no programmable inputs, so stimulus is unavailable.";
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
            ? $"Programmable inputs set to 1 at Logical Time {logicalTime}."
            : $"Stimulus rejected: {((WorkspaceCommandRejected)outcome).Code}.";
    }

    private async Task Step()
    {
        var precondition = SessionPrecondition();
        var outcome = await Execute(context => new StepSession(context, precondition));
        StimulusIsScheduled = false;
        Status = outcome switch
        {
            SessionStepped stepped =>
                $"Step committed at Logical Time {stepped.LogicalTime}.",
            SessionAdvanceFailed failed =>
                $"Step failed: {AdvanceFailureText(failed.Failure.Reason)}.",
            WorkspaceCommandRejected rejected => $"Step rejected: {rejected.Code}.",
            _ => "Step failed: workspace internal defect.",
        };
    }

    private static string AdvanceFailureText(AdvanceFailureReason reason)
    {
        return reason switch
        {
            AdvanceFailureReason.ZeroTimeOscillation => "zero-time oscillation",
            AdvanceFailureReason.SimulationResourceLimit => "simulation resource limit",
            AdvanceFailureReason.SimulationCancelled => "simulation cancelled",
            AdvanceFailureReason.SimulationInfrastructureFailure =>
                "simulation infrastructure failure",
            AdvanceFailureReason.SimulationInternalDefect => "simulation internal defect",
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

        Status = $"Authoring rejected: {((WorkspaceCommandRejected)outcome).Code}.";
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

        var caller = CurrentCaller;
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
        return true;
    }

    private async Task<bool> CanPublishAttachmentAsync(
        Attached attached,
        WorkspaceCaller caller,
        Attached? expectedCurrentAttachment = null)
    {
        var attachmentWasSuperseded = expectedCurrentAttachment is not null
            && !HasCurrentFence(expectedCurrentAttachment);
        var authorizationChanged = attached.Projection.Durability
                is not SandboxWorkspaceDurabilityProjection
            && caller != CurrentCaller;
        if (Volatile.Read(ref isDisposed) == 0
            && !attachmentWasSuperseded
            && !authorizationChanged)
        {
            return true;
        }

        if (authorizationChanged)
        {
            ShowAttachmentFailure("workspace_authorization_changed");
            Status = "Authentication changed. Reload the Workspace to continue.";
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

        var caller = CurrentCaller;
        var read = await workspace.ReadAsync(
            QueryContext(),
            ReadProjection.Instance,
            cancellationToken);
        if (caller != CurrentCaller
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
        Status = $"Sandbox Workspace closed: {rejectionCode}. Create a new Sandbox Project.";
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
        AttachmentFailure = WorkspaceAttachmentFailure.From(code);
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
            componentLifetime.Dispose();
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
            CurrentCaller);
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
            CurrentCaller);
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
        Status = "The accessible Scene exceeds the active Port projection budget.";
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
        public static WorkspaceAttachmentFailure From(string code)
        {
            ArgumentException.ThrowIfNullOrEmpty(code);
            return code switch
            {
                WorkspaceOutcomeReasons.WorkspaceNotFound
                    or WorkspaceOutcomeReasons.WorkspaceExpired => new(
                    code,
                    "This workspace is no longer available.",
                    "It may have expired or been closed. Return to your Durable Projects, or start a new Sandbox."),
                "workspace_authorization_failed" or "workspace_authorization_changed" => new(
                    code,
                    "Your access to this workspace changed.",
                    "Sign in with the project owner account and reopen it from Durable Projects."),
                WorkspaceOutcomeReasons.StaleWorkspaceAttachment => new(
                    code,
                    "This workspace is attached elsewhere.",
                    "Continue in the tab that owns the active attachment, or reopen an authorized Durable Project."),
                WorkspaceOutcomeReasons.BuildFingerprintMismatch => new(
                    code,
                    "Logic Lab was updated.",
                    "Reload the application before reopening this workspace."),
                _ => new(
                    code,
                    "We couldn't open this workspace.",
                    "Return to your Durable Projects and try reopening it, or start a new Sandbox."),
            };
        }
    }
}
