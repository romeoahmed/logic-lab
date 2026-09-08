using System.Diagnostics;
using LogicLab.Application.Workspaces;
using LogicLab.Web.Components.Editor;
using LogicLab.Web.Transfers;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Localization;
using Microsoft.JSInterop;

namespace LogicLab.Web.Components.Pages;

public sealed partial class Editor : IAsyncDisposable
{
    private static readonly TimeSpan ProjectionRefreshInterval =
        TimeSpan.FromMilliseconds(250);
    private readonly IEditorWorkspace workspace;
    private readonly ProjectImportWorkflow projectImportWorkflow;
    private readonly TimeProvider timeProvider;
    private readonly CancellationTokenSource componentLifetime = new();
    private readonly CancellationToken componentCancellationToken;
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
        componentCancellationToken = componentLifetime.Token;
    }

    private WorkspaceProjection? Projection { get; set; }

    private Attached? Attachment { get; set; }

    private WorkspaceAttachmentFailure? AttachmentFailure { get; set; }

    private bool IsInteractive { get; set; }

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
        if (Volatile.Read(ref isDisposed) != 0)
        {
            return;
        }
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

        if (CommandsAvailable && IsSimulationRunning
            && (runObservation is null || runObservation.IsCompleted))
        {
            runObservation = ObserveRunAsync(componentCancellationToken);
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
            .ReadHistoryEntryStateAsync(workspaceLocator, componentCancellationToken);
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
            componentCancellationToken);
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
                componentCancellationToken);
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

    private Task CreateProject() => OpenInitialWorkspaceAsync(
        new CreateSandbox("Sandbox Project", "Main", RequireCurrentCaller()),
        Text["StatusSandboxCreated"]);

    private async Task OpenInitialWorkspaceAsync(OpenWorkspaceRequest request, string successStatus)
    {
        var caller = request.Caller;
        var outcome = await workspace.OpenAsync(request, componentCancellationToken);
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
            componentCancellationToken);
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
        Status = successStatus;
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
            componentCancellationToken);
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
        if (Volatile.Read(ref isDisposed) != 0
            || !IsCallerAvailable
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
        SelectedDefinitionId = null;
        HierarchyNavigation.Clear();
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
        // A delayed observation must not undo a newer command publication.
        if (Projection is { } current
            && current.WorkspaceId == projection.WorkspaceId
            && projection.ProjectionVersion < current.ProjectionVersion)
        {
            return;
        }

        var projectRevisionChanged = Projection?.ProjectRevision.RevisionId
            != projection.ProjectRevision.RevisionId;
        Projection = projection;
        if (projectRevisionChanged)
        {
            PreparedExportUrl = null;
            ProjectScene();
            return;
        }

        EnsureSceneToolAvailable();
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
            if (runObservation is not null)
            {
                await runObservation;
            }

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
