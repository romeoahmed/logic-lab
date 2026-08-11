using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using LogicLab.Application.Workspaces;
using LogicLab.Web.Components.Editor;
using LogicLab.Web.Components.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;

namespace LogicLab.Web.Tests;

internal sealed class EditorDurableRouteTests
{
    [Test]
    public async Task Editor_BuildMismatch_OffersHardReloadRecovery()
    {
        await using var context = new BunitContext();
        await using var workspace = new RecordingAttachWorkspace(
            buildFingerprint: "previous-build");
        var opened = (WorkspaceOpened)await workspace.OpenAsync(
            new CreateSandbox("Previous build project", "Main"),
            CancellationToken.None);
        Configure(context, workspace);

        var rendered = RenderEditor(
            context,
            opened.WorkspaceId,
            AuthenticationStateFor(null));

        var reload = await rendered.WaitForElementAsync(
            "[data-recovery='reload']");
        await reload.ClickAsync();
        var navigation = (BunitNavigationManager)context.Services
            .GetRequiredService<NavigationManager>();

        using (Assert.Multiple())
        {
            await Assert.That(rendered.Find("[data-workspace-attachment-error]")
                    .GetAttribute("data-error-code"))
                .IsEqualTo("build_fingerprint_mismatch");
            await Assert.That(navigation.History).Count().IsEqualTo(1);
            await Assert.That(navigation.History.Single().Options.ForceLoad).IsTrue();
        }
    }

    [Test]
    public async Task Editor_MissingWorkspace_RendersRecoveryStateInsteadOfSandboxWorkbench()
    {
        await using var context = new BunitContext();
        await using var workspace = new RecordingAttachWorkspace();
        Configure(context, workspace);

        var rendered = RenderEditor(
            context,
            new WorkspaceId("missing-workspace"),
            AuthenticationStateFor(null));

        var recovery = await rendered.WaitForElementAsync(
            "[data-workspace-attachment-error]");
        using (Assert.Multiple())
        {
            await Assert.That(recovery.GetAttribute("data-error-code"))
                .IsEqualTo("workspace_not_found");
            await Assert.That(rendered.FindAll("[data-command]")).IsEmpty();
            await Assert.That(rendered.FindAll("section[aria-label='Circuit workbench']"))
                .IsEmpty();
            await Assert.That(rendered.Find("[data-recovery='projects']")
                    .GetAttribute("href"))
                .IsEqualTo("/projects");
            await Assert.That(rendered.Find("[data-recovery='sandbox']")
                    .GetAttribute("href"))
                .IsEqualTo("/editor");
        }
    }

    [Test]
    [Arguments(null)]
    [Arguments("replacement-subject")]
    public async Task Editor_SandboxRoute_AuthenticationSubjectChanges_PreservesAttachmentAndUsesCurrentCaller(
        string? replacementSubject)
    {
        await using var context = new BunitContext();
        await using var workspace = new RecordingAttachWorkspace();
        var opened = (WorkspaceOpened)await workspace.OpenAsync(
            new CreateSandbox("Reopened project", "Main"),
            CancellationToken.None);
        Configure(context, workspace);
        var rendered = RenderEditor(
            context,
            opened.WorkspaceId,
            AuthenticationStateFor("subject-editor"));
        await rendered.WaitForElementAsync(
            "[data-command='author']:not([disabled])");
        var attached = (Attached)workspace.AttachOutcomes.Single();
        var initialProjection = rendered.FindComponent<WorkbenchStatusStrip>()
            .Instance.Projection!;
        var initialScene = rendered.FindComponent<AccessibleCircuitScene>()
            .Instance.Scene!;

        rendered.Render(parameters => parameters
            .Add(value => value.Value, AuthenticationStateFor(replacementSubject))
            .Add(value => value.ChildContent, (RenderFragment)(builder =>
            {
                builder.OpenComponent<Editor>(0);
                builder.AddAttribute(1, nameof(Editor.WorkspaceIdValue), opened.WorkspaceId.Value);
                builder.CloseComponent();
            })));

        await rendered.WaitForElementAsync(
            "[data-command='author']:not([disabled])");
        var retainedProjection = rendered.FindComponent<WorkbenchStatusStrip>()
            .Instance.Projection!;
        var retainedScene = rendered.FindComponent<AccessibleCircuitScene>()
            .Instance.Scene!;
        using (Assert.Multiple())
        {
            await Assert.That(rendered.Find("[data-command='author']")
                    .HasAttribute("disabled"))
                .IsFalse();
            await Assert.That(rendered.Find("[data-command='create']")
                    .HasAttribute("disabled"))
                .IsTrue();
            await Assert.That(workspace.DetachRequest).IsNull();
            await Assert.That(workspace.AttachRequests).Count().IsEqualTo(1);
            await Assert.That(retainedProjection.WorkspaceId)
                .IsEqualTo(initialProjection.WorkspaceId);
            await Assert.That(retainedProjection.ProjectRevision.RevisionId)
                .IsEqualTo(initialProjection.ProjectRevision.RevisionId);
            await Assert.That(retainedScene).IsNotNull();
            await Assert.That(retainedScene.CircuitDefinitionId)
                .IsEqualTo(initialScene.CircuitDefinitionId);
        }

        await rendered.Find("[data-command='author']").ClickAsync();
        await rendered.WaitForElementAsync(
            "[data-command='compile']:not([disabled])");

        var command = workspace.LastCommand!;
        using (Assert.Multiple())
        {
            await Assert.That(workspace.DetachRequest).IsNull();
            await Assert.That(command.Context.AttachmentId)
                .IsEqualTo(attached.AttachmentId);
            await Assert.That(command.Context.AttachmentGeneration)
                .IsEqualTo(attached.Generation);
            await Assert.That(rendered.FindComponent<AccessibleCircuitScene>()
                    .Instance.Scene)
                .IsNotNull();
        }

        if (replacementSubject is null)
        {
            await Assert.That(command.Context.Caller)
                .IsTypeOf<AnonymousWorkspaceCaller>();
        }
        else
        {
            var caller = (await Assert.That(command.Context.Caller)
                .IsTypeOf<AuthenticatedWorkspaceCaller>())!;
            await Assert.That(caller.SubjectId.Value)
                .IsEqualTo(replacementSubject);
        }
    }

    [Test]
    public async Task Editor_ClaimedSandbox_AuthenticationExpires_ClearsProjectionAndDetaches()
    {
        await using var context = new BunitContext();
        await using var workspace = new RecordingAttachWorkspace();
        var opened = (WorkspaceOpened)await workspace.OpenAsync(
            new CreateSandbox("Claimed project", "Main"),
            CancellationToken.None);
        Configure(context, workspace);
        var rendered = RenderEditor(
            context,
            opened.WorkspaceId,
            AuthenticationStateFor("subject-editor"));
        await rendered.WaitForElementAsync(
            "[data-command='author']:not([disabled])");
        var attached = (Attached)workspace.AttachOutcomes.Single();

        workspace.ProjectReadsAsDurable();
        await rendered.Find("[data-command='author']").ClickAsync();
        await rendered.WaitForStateAsync(() => rendered
            .FindComponent<WorkbenchStatusStrip>()
            .Instance.Projection?.Durability
            is DurableWorkspaceDurabilityProjection);

        rendered.Render(parameters => parameters
            .Add(value => value.Value, AuthenticationStateFor(null))
            .Add(value => value.ChildContent, (RenderFragment)(builder =>
            {
                builder.OpenComponent<Editor>(0);
                builder.AddAttribute(1, nameof(Editor.WorkspaceIdValue), opened.WorkspaceId.Value);
                builder.CloseComponent();
            })));

        var detach = (await Assert.That(workspace.DetachRequest)
            .IsNotNull())!;
        var detachCaller = (await Assert.That(detach.Caller)
            .IsTypeOf<AuthenticatedWorkspaceCaller>())!;
        using (Assert.Multiple())
        {
            await Assert.That(ShowsWorkspaceRecoveryWithoutEditor(rendered)).IsTrue();
            await Assert.That(detach.WorkspaceId).IsEqualTo(opened.WorkspaceId);
            await Assert.That(detach.AttachmentId).IsEqualTo(attached.AttachmentId);
            await Assert.That(detach.AttachmentGeneration)
                .IsEqualTo(attached.Generation);
            await Assert.That(detachCaller.SubjectId.Value)
                .IsEqualTo("subject-editor");
        }
    }

    [Test]
    public async Task Editor_SandboxRoute_AuthenticationChangesDuringReattach_PublishesNewFenceAndContinues()
    {
        await using var context = new BunitContext();
        await using var workspace = new RecordingAttachWorkspace(
            blockFirstReattach: true,
            rejectFirstDispatchWithReattach: true);
        var opened = (WorkspaceOpened)await workspace.OpenAsync(
            new CreateSandbox("Reopened project", "Main"),
            CancellationToken.None);
        Configure(context, workspace);
        var rendered = RenderEditor(
            context,
            opened.WorkspaceId,
            AuthenticationStateFor("subject-editor"));
        await rendered.WaitForElementAsync(
            "[data-command='author']:not([disabled])");
        var initialAttachment = (Attached)workspace.AttachOutcomes.Single();

        var authoring = rendered.Find("[data-command='author']").ClickAsync();
        await workspace.ReattachStarted.WaitAsync(TimeSpan.FromSeconds(5));
        rendered.Render(parameters => parameters
            .Add(value => value.Value, AuthenticationStateFor(null))
            .Add(value => value.ChildContent, (RenderFragment)(builder =>
            {
                builder.OpenComponent<Editor>(0);
                builder.AddAttribute(1, nameof(Editor.WorkspaceIdValue), opened.WorkspaceId.Value);
                builder.CloseComponent();
            })));
        workspace.ReleaseReattach();
        await authoring;
        await rendered.WaitForElementAsync(
            "[data-command='compile']:not([disabled])");

        var reattached = (Attached)workspace.AttachOutcomes.Last();
        var command = workspace.LastCommand!;
        using (Assert.Multiple())
        {
            await Assert.That(workspace.DetachRequest).IsNull();
            await Assert.That(workspace.AttachRequests).Count().IsEqualTo(2);
            await Assert.That(workspace.AttachRequests[1]).IsTypeOf<Reattach>();
            await Assert.That(reattached.AttachmentId)
                .IsNotEqualTo(initialAttachment.AttachmentId);
            await Assert.That(reattached.Generation)
                .IsEqualTo(initialAttachment.Generation + 1);
            await Assert.That(command.Context.AttachmentId)
                .IsEqualTo(reattached.AttachmentId);
            await Assert.That(command.Context.AttachmentGeneration)
                .IsEqualTo(reattached.Generation);
            await Assert.That(command.Context.Caller)
                .IsTypeOf<AnonymousWorkspaceCaller>();
            await Assert.That(rendered.FindComponent<WorkbenchStatusStrip>()
                    .Instance.Projection)
                .IsNotNull();
            await Assert.That(rendered.FindComponent<AccessibleCircuitScene>()
                    .Instance.Scene)
                .IsNotNull();
        }
    }

    [Test]
    [Arguments(null)]
    [Arguments("replacement-subject")]
    public async Task Editor_DurableRoute_AuthenticationSubjectChanges_ClearsProjectionAndDetachesAsPriorSubject(
        string? replacementSubject)
    {
        await using var context = new BunitContext();
        var owner = new AuthenticatedWorkspaceCaller(
            new AuthenticatedSubjectId("subject-editor"));
        var projectId = new DurableProjectId("durable-auth-change");
        await using var workspace = new RecordingAttachWorkspace(
            durableProjectLoader: DurableLoader(projectId));
        var opened = (WorkspaceOpened)await workspace.OpenAsync(
            new OpenDurable(projectId, owner),
            CancellationToken.None);
        Configure(context, workspace);
        var rendered = RenderEditor(
            context,
            opened.WorkspaceId,
            AuthenticationStateFor("subject-editor"));
        await rendered.WaitForElementAsync(
            "[data-command='session']:not([disabled])");
        var attached = (Attached)workspace.AttachOutcomes.Single();

        rendered.Render(parameters => parameters
            .Add(value => value.Value, AuthenticationStateFor(replacementSubject))
            .Add(value => value.ChildContent, (RenderFragment)(builder =>
            {
                builder.OpenComponent<Editor>(0);
                builder.AddAttribute(1, nameof(Editor.WorkspaceIdValue), opened.WorkspaceId.Value);
                builder.CloseComponent();
            })));

        await rendered.WaitForStateAsync(() => workspace.DetachRequest is not null);
        var detach = workspace.DetachRequest!;
        using (Assert.Multiple())
        {
            await Assert.That(ShowsWorkspaceRecoveryWithoutEditor(rendered)).IsTrue();
            await Assert.That(((AuthenticatedWorkspaceCaller)detach.Caller)
                    .SubjectId.Value)
                .IsEqualTo("subject-editor");
            await Assert.That(detach.WorkspaceId).IsEqualTo(opened.WorkspaceId);
            await Assert.That(detach.AttachmentId).IsEqualTo(attached.AttachmentId);
            await Assert.That(detach.AttachmentGeneration)
                .IsEqualTo(attached.Generation);
        }
    }

    [Test]
    public async Task Editor_DurableRoute_AuthenticationChangesDuringAttach_DiscardsAndDetachesPriorSubjectOutcome()
    {
        await using var context = new BunitContext();
        var owner = new AuthenticatedWorkspaceCaller(
            new AuthenticatedSubjectId("subject-editor"));
        var projectId = new DurableProjectId("durable-auth-attach-race");
        await using var workspace = new RecordingAttachWorkspace(
            blockFirstAttach: true,
            durableProjectLoader: DurableLoader(projectId));
        var opened = (WorkspaceOpened)await workspace.OpenAsync(
            new OpenDurable(projectId, owner),
            CancellationToken.None);
        Configure(context, workspace);
        var rendered = RenderEditor(
            context,
            opened.WorkspaceId,
            AuthenticationStateFor("subject-editor"));
        await workspace.AttachStarted.WaitAsync(TimeSpan.FromSeconds(5));

        rendered.Render(parameters => parameters
            .Add(value => value.Value, AuthenticationStateFor("replacement-subject"))
            .Add(value => value.ChildContent, (RenderFragment)(builder =>
            {
                builder.OpenComponent<Editor>(0);
                builder.AddAttribute(1, nameof(Editor.WorkspaceIdValue), opened.WorkspaceId.Value);
                builder.CloseComponent();
            })));
        workspace.ReleaseAttach();

        await rendered.WaitForStateAsync(() => workspace.DetachRequest is not null);
        var attached = (Attached)workspace.AttachOutcomes.Single();
        await Assert.That(ShowsWorkspaceRecoveryWithoutEditor(rendered)).IsTrue();
        await Assert.That(workspace.DetachRequest).IsNotNull();
        var detach = workspace.DetachRequest!;
        using (Assert.Multiple())
        {
            await Assert.That(((AuthenticatedWorkspaceCaller)detach.Caller)
                    .SubjectId.Value)
                .IsEqualTo("subject-editor");
            await Assert.That(detach.WorkspaceId).IsEqualTo(opened.WorkspaceId);
            await Assert.That(detach.AttachmentId).IsEqualTo(attached.AttachmentId);
            await Assert.That(detach.AttachmentGeneration)
                .IsEqualTo(attached.Generation);
        }
    }

    [Test]
    public async Task Editor_DurableRoute_AuthenticationChangesDuringRead_DiscardsPriorSubjectSnapshot()
    {
        await using var context = new BunitContext();
        var owner = new AuthenticatedWorkspaceCaller(
            new AuthenticatedSubjectId("subject-editor"));
        var projectId = new DurableProjectId("durable-auth-read-race");
        await using var workspace = new RecordingAttachWorkspace(
            durableProjectLoader: DurableLoader(projectId));
        var opened = (WorkspaceOpened)await workspace.OpenAsync(
            new OpenDurable(projectId, owner),
            CancellationToken.None);
        Configure(context, workspace);
        var rendered = RenderEditor(
            context,
            opened.WorkspaceId,
            AuthenticationStateFor("subject-editor"));
        await rendered.WaitForElementAsync(
            "[data-command='session']:not([disabled])");
        var attached = (Attached)workspace.AttachOutcomes.Single();
        workspace.BlockNextRead();

        var sessionCreation = rendered.Find("[data-command='session']").ClickAsync();
        await workspace.ReadStarted.WaitAsync(TimeSpan.FromSeconds(5));
        rendered.Render(parameters => parameters
            .Add(value => value.Value, AuthenticationStateFor(null))
            .Add(value => value.ChildContent, (RenderFragment)(builder =>
            {
                builder.OpenComponent<Editor>(0);
                builder.AddAttribute(1, nameof(Editor.WorkspaceIdValue), opened.WorkspaceId.Value);
                builder.CloseComponent();
            })));
        workspace.ReleaseRead();
        await sessionCreation;

        await Assert.That(workspace.BlockedReadOutcome)
            .IsTypeOf<ProjectionSnapshot>();
        await Assert.That(ShowsWorkspaceRecoveryWithoutEditor(rendered)).IsTrue();
        using (Assert.Multiple())
        {
            await Assert.That(((AuthenticatedWorkspaceCaller)workspace.ReadCaller!)
                    .SubjectId.Value)
                .IsEqualTo("subject-editor");
            await Assert.That(((AuthenticatedWorkspaceCaller)workspace.DetachCaller!)
                    .SubjectId.Value)
                .IsEqualTo("subject-editor");
            await Assert.That(workspace.DetachRequest?.AttachmentId)
                .IsEqualTo(attached.AttachmentId);
            await Assert.That(workspace.DetachRequest?.AttachmentGeneration)
                .IsEqualTo(attached.Generation);
        }
    }

    [Test]
    public async Task Editor_DurableRoute_ReportsPersistentSaveState()
    {
        await using var context = new BunitContext();
        var owner = new AuthenticatedWorkspaceCaller(
            new AuthenticatedSubjectId("subject-editor"));
        var projectId = new DurableProjectId("durable-save-status");
        await using var workspace = new RecordingAttachWorkspace(
            durableProjectLoader: DurableLoader(projectId));
        var opened = (WorkspaceOpened)await workspace.OpenAsync(
            new OpenDurable(projectId, owner),
            CancellationToken.None);
        Configure(context, workspace);

        var rendered = RenderEditor(
            context,
            opened.WorkspaceId,
            AuthenticationStateFor("subject-editor"));
        var saveStatus = await rendered.WaitForElementAsync(
            "[data-status='save'] dd");

        using (Assert.Multiple())
        {
            await Assert.That(saveStatus.TextContent).Contains("Durable");
            await Assert.That(saveStatus.TextContent).DoesNotContain("Sandbox");
        }
    }

    [Test]
    public async Task Editor_DurableRoute_FreshComponentRecoversAttachmentAndFencesPriorComponent()
    {
        await using var context = new BunitContext();
        var owner = new AuthenticatedWorkspaceCaller(
            new AuthenticatedSubjectId("subject-editor"));
        var projectId = new DurableProjectId("durable-editor-route");
        var revision = WebTestCircuit.CreateCompleteCircuit();
        await using var workspace = new RecordingAttachWorkspace(
            durableProjectLoader: new FixedDurableProjectLoader(
                new DurableProjectOpenFound(
                    projectId,
                    new DurableDisplayName("Reopened project"),
                    new DurableVersion("version-1"),
                    revision)));
        var opened = (WorkspaceOpened)await workspace.OpenAsync(
            new OpenDurable(projectId, owner),
            CancellationToken.None);
        Configure(context, workspace);
        var authenticationState = AuthenticationStateFor("subject-editor");
        var first = RenderEditor(context, opened.WorkspaceId, authenticationState);
        await first.WaitForElementAsync("[data-command='session']:not([disabled])");

        var second = RenderEditor(context, opened.WorkspaceId, authenticationState);
        await second.WaitForElementAsync("[data-command='session']:not([disabled])");
        await second.WaitForStateAsync(() => workspace.AttachRequests.Length == 3);

        await first.Find("[data-command='session']").ClickAsync();
        await first.WaitForStateAsync(() => workspace.LastCommandOutcome is not null);

        var stale = (await Assert.That(workspace.LastCommandOutcome)
            .IsTypeOf<WorkspaceCommandRejected>())!;
        var recover = (RecoverAttach)workspace.AttachRequests[2];
        using (Assert.Multiple())
        {
            await Assert.That(workspace.AttachRequests[0]).IsTypeOf<InitialAttach>();
            await Assert.That(workspace.AttachRequests[1]).IsTypeOf<InitialAttach>();
            await Assert.That(workspace.AttachRequests[2]).IsTypeOf<RecoverAttach>();
            await Assert.That(workspace.AttachOutcomes[1])
                .IsTypeOf<AttachRejected>();
            await Assert.That(((AttachRejected)workspace.AttachOutcomes[1]).Code)
                .IsEqualTo("stale_workspace_attachment");
            await Assert.That(((AttachRejected)workspace.AttachOutcomes[1])
                    .RetryDisposition)
                .IsEqualTo(RetryDisposition.Reattach);
            await Assert.That(((Attached)workspace.AttachOutcomes[2]).Generation)
                .IsEqualTo(((Attached)workspace.AttachOutcomes[0]).Generation + 1);
            await Assert.That(((Attached)workspace.AttachOutcomes[2]).AttachmentId)
                .IsNotEqualTo(((Attached)workspace.AttachOutcomes[0]).AttachmentId);
            await Assert.That(((AuthenticatedWorkspaceCaller)recover.Caller)
                    .SubjectId.Value)
                .IsEqualTo("subject-editor");
            await Assert.That(stale.Code).IsEqualTo("stale_workspace_attachment");
        }
    }

    [Test]
    public async Task Editor_DurableRoute_UsesCurrentSubjectForAttachmentLifetime()
    {
        await using var context = new BunitContext();
        await using var workspace = new RecordingAttachWorkspace();
        var opened = (WorkspaceOpened)await workspace.OpenAsync(
            new CreateSandbox("Reopened project", "Main"),
            CancellationToken.None);
        context.Services.AddFluentUIComponents();
        context.Services.AddSingleton(TimeProvider.System);
        context.Services.AddSingleton<IEditorWorkspace>(workspace);
        context.Renderer.SetRendererInfo(new RendererInfo("Server", isInteractive: true));
        var authenticationState = Task.FromResult(new AuthenticationState(
            new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "subject-editor")],
                authenticationType: "Tests"))));

        var rendered = context.Render<CascadingValue<Task<AuthenticationState>>>(
            parameters => parameters
                .Add(value => value.Value, authenticationState)
                .AddChildContent<Editor>(editor => editor
                    .Add(component => component.WorkspaceIdValue,
                        opened.WorkspaceId.Value)));
        await rendered.WaitForElementAsync("[data-command='author']:not([disabled])");
        await rendered.Find("[data-command='author']").ClickAsync();
        await rendered.WaitForStateAsync(() => workspace.ReadCaller is not null);
        await rendered.FindComponent<Editor>().Instance.DisposeAsync();

        var attach = (await Assert.That(workspace.Request)
            .IsTypeOf<InitialAttach>())!;
        using (Assert.Multiple())
        {
            await Assert.That(((AuthenticatedWorkspaceCaller)attach.Caller)
                    .SubjectId.Value)
                .IsEqualTo("subject-editor");
            await Assert.That(((AuthenticatedWorkspaceCaller)workspace.CommandCaller!)
                    .SubjectId.Value)
                .IsEqualTo("subject-editor");
            await Assert.That(((AuthenticatedWorkspaceCaller)workspace.ReadCaller!)
                    .SubjectId.Value)
                .IsEqualTo("subject-editor");
            await Assert.That(((AuthenticatedWorkspaceCaller)workspace.DetachCaller!)
                    .SubjectId.Value)
                .IsEqualTo("subject-editor");
        }
    }

    private static void Configure(
        BunitContext context,
        IEditorWorkspace workspace)
    {
        context.Services.AddFluentUIComponents();
        context.Services.AddSingleton(TimeProvider.System);
        context.Services.AddSingleton(workspace);
        context.Renderer.SetRendererInfo(new RendererInfo("Server", isInteractive: true));
    }

    private static IRenderedComponent<CascadingValue<Task<AuthenticationState>>> RenderEditor(
        BunitContext context,
        WorkspaceId workspaceId,
        Task<AuthenticationState> authenticationState)
    {
        return context.Render<CascadingValue<Task<AuthenticationState>>>(
            parameters => parameters
                .Add(value => value.Value, authenticationState)
                .AddChildContent<Editor>(editor => editor
                    .Add(component => component.WorkspaceIdValue, workspaceId.Value)));
    }

    private static Task<AuthenticationState> AuthenticationStateFor(string? subjectId)
    {
        var claims = subjectId is null
            ? Array.Empty<Claim>()
            : [new Claim(ClaimTypes.NameIdentifier, subjectId)];
        return Task.FromResult(new AuthenticationState(
            new ClaimsPrincipal(new ClaimsIdentity(
                claims,
                subjectId is null ? null : "Tests"))));
    }

    private static bool ShowsWorkspaceRecoveryWithoutEditor(
        IRenderedComponent<CascadingValue<Task<AuthenticationState>>> rendered)
    {
        return rendered.FindAll("[data-workspace-attachment-error]").Count == 1
            && rendered.FindAll("[data-command]").Count == 0
            && rendered.FindComponents<WorkbenchStatusStrip>().Count == 0
            && rendered.FindComponents<AccessibleCircuitScene>().Count == 0;
    }

    private static FixedDurableProjectLoader DurableLoader(DurableProjectId projectId)
    {
        return new FixedDurableProjectLoader(
            new DurableProjectOpenFound(
                projectId,
                new DurableDisplayName("Reopened project"),
                new DurableVersion("version-1"),
                WebTestCircuit.CreateCompleteCircuit()));
    }

    private sealed class RecordingAttachWorkspace(
        bool blockFirstAttach = false,
        bool blockFirstReattach = false,
        bool rejectFirstDispatchWithReattach = false,
        IDurableProjectLoader? durableProjectLoader = null,
        string? buildFingerprint = null)
        : DelegatingEditorWorkspace(
            durableProjectLoader: durableProjectLoader,
            buildFingerprint: buildFingerprint)
    {
        private readonly object gate = new();
        private readonly List<AttachRequest> attachRequests = [];
        private readonly List<WorkspaceAttachOutcome> attachOutcomes = [];
        private readonly TaskCompletionSource attachStarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource releaseAttach = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource reattachStarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource releaseReattach = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource readStarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource releaseRead = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int shouldBlockAttach = blockFirstAttach ? 1 : 0;
        private int shouldBlockReattach = blockFirstReattach ? 1 : 0;
        private int shouldRejectDispatch = rejectFirstDispatchWithReattach ? 1 : 0;
        private int shouldBlockRead;
        private int projectReadsAsDurable;

        public AttachRequest? Request { get; private set; }

        public AttachRequest[] AttachRequests
        {
            get
            {
                lock (gate)
                {
                    return [.. attachRequests];
                }
            }
        }

        public WorkspaceAttachOutcome[] AttachOutcomes
        {
            get
            {
                lock (gate)
                {
                    return [.. attachOutcomes];
                }
            }
        }

        public WorkspaceCaller? CommandCaller { get; private set; }

        public WorkspaceCommand? LastCommand { get; private set; }

        public WorkspaceCaller? ReadCaller { get; private set; }

        public WorkspaceCaller? DetachCaller { get; private set; }

        public DetachRequest? DetachRequest { get; private set; }

        public WorkspaceCommandOutcome? LastCommandOutcome { get; private set; }

        public WorkspaceReadOutcome? BlockedReadOutcome { get; private set; }

        public Task AttachStarted => attachStarted.Task;

        public Task ReattachStarted => reattachStarted.Task;

        public Task ReadStarted => readStarted.Task;

        public void ReleaseAttach() => releaseAttach.TrySetResult();

        public void ReleaseReattach() => releaseReattach.TrySetResult();

        public void BlockNextRead() => Volatile.Write(ref shouldBlockRead, 1);

        public void ProjectReadsAsDurable()
            => Volatile.Write(ref projectReadsAsDurable, 1);

        public void ReleaseRead() => releaseRead.TrySetResult();

        public override async Task<WorkspaceAttachOutcome> AttachAsync(
            AttachRequest request,
            CancellationToken cancellationToken)
        {
            Request = request;
            if (Interlocked.Exchange(ref shouldBlockAttach, 0) != 0)
            {
                attachStarted.TrySetResult();
                await releaseAttach.Task.WaitAsync(cancellationToken);
            }

            var outcome = await base.AttachAsync(request, cancellationToken);
            if (request is Reattach
                && Interlocked.Exchange(ref shouldBlockReattach, 0) != 0)
            {
                reattachStarted.TrySetResult();
                await releaseReattach.Task.WaitAsync(cancellationToken);
            }

            lock (gate)
            {
                attachRequests.Add(request);
                attachOutcomes.Add(outcome);
            }

            return outcome;
        }

        public override async Task<WorkspaceCommandOutcome> DispatchAsync(
            WorkspaceCommand command,
            CancellationToken cancellationToken)
        {
            LastCommand = command;
            CommandCaller = command.Context.Caller;
            LastCommandOutcome = Interlocked.Exchange(ref shouldRejectDispatch, 0) != 0
                ? new WorkspaceCommandRejected(
                    "idempotency_window_closed",
                    [],
                    RetryDisposition.Reattach)
                : await base.DispatchAsync(command, cancellationToken);
            return LastCommandOutcome;
        }

        public override async Task<WorkspaceReadOutcome> ReadAsync(
            WorkspaceQueryContext context,
            WorkspaceQuery query,
            CancellationToken cancellationToken)
        {
            ReadCaller = context.Caller;
            var outcome = await base.ReadAsync(context, query, cancellationToken);
            if (outcome is ProjectionSnapshot snapshot
                && Volatile.Read(ref projectReadsAsDurable) != 0)
            {
                outcome = new ProjectionSnapshot(snapshot.Projection with
                {
                    Durability = new DurableWorkspaceDurabilityProjection(
                        new DurableProjectId("claimed-project"),
                        new DurableVersion("claimed-version"),
                        snapshot.Projection.ProjectRevision.RevisionId,
                        DurableSaveStatus.Clean,
                        conflictActualDurableVersion: null),
                });
            }

            if (Interlocked.Exchange(ref shouldBlockRead, 0) != 0)
            {
                BlockedReadOutcome = outcome;
                readStarted.TrySetResult();
                await releaseRead.Task.WaitAsync(cancellationToken);
            }

            return outcome;
        }

        public override Task<WorkspaceDetachOutcome> DetachAsync(
            DetachRequest request,
            CancellationToken cancellationToken)
        {
            DetachCaller = request.Caller;
            DetachRequest = request;
            return base.DetachAsync(request, cancellationToken);
        }
    }

    private sealed class FixedDurableProjectLoader(
        DurableProjectOpenRepositoryOutcome outcome) : IDurableProjectLoader
    {
        public Task<DurableProjectOpenRepositoryOutcome> LoadAsync(
            DurableProjectOpenRequest request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(outcome);
        }
    }
}
