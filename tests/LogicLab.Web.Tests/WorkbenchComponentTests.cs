using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using LogicLab.Application.Workspaces;
using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.Engine.Compilation;
using LogicLab.ProjectFormat;
using LogicLab.Web.Components.Editor;
using LogicLab.Web.Components.Pages;
using LogicLab.Web.Transfers;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;

namespace LogicLab.Web.Tests;

internal sealed class WorkbenchComponentTests
{
    [Test]
    public async Task Editor_ValidLogicLabUpload_OpensCompiledImportedWorkspace()
    {
        await using var context = CreateContext();
        await using var workspace = new PassthroughWorkspace();
        var rendered = RenderEditor(context, workspace);
        var navigation = (BunitNavigationManager)context.Services
            .GetRequiredService<NavigationManager>();
        _ = await rendered.WaitForElementAsync("[data-command='import']:not([disabled])");
        var package = await CreatePackageAsync();

        rendered.FindComponent<InputFile>().UploadFiles(
            InputFileContent.CreateFromBinary(
                package,
                "project.logiclab",
                contentType: "application/vnd.logiclab+zip"));

        await rendered.WaitForStateAsync(() =>
            navigation.Uri.Contains("/editor/", StringComparison.Ordinal));
        var importedWorkspaceId = new WorkspaceId(
            new Uri(navigation.Uri).Segments[^1].TrimEnd('/'));
        var attach = await workspace.AttachAsync(
            new InitialAttach(
                importedWorkspaceId,
                LogicLabWebBuild.Fingerprint,
                AnonymousWorkspaceCaller.Instance),
            CancellationToken.None);
        var attached = (await Assert.That(attach).IsTypeOf<Attached>())!;
        using (Assert.Multiple())
        {
            await Assert.That(navigation.Uri).StartsWith("http://localhost/editor/");
            await Assert.That(attached.Projection.ProjectRevision.Document.DisplayName)
                .IsEqualTo("Uploaded project");
            await Assert.That(attached.Projection.Compilation)
                .IsTypeOf<CompilationPublishedProjection>();
        }
    }

    [Test]
    public async Task Editor_InvalidLogicLabUpload_ReportsRejectionAndKeepsCurrentPage()
    {
        await using var context = CreateContext();
        await using var workspace = new PassthroughWorkspace();
        var rendered = RenderEditor(context, workspace);
        var navigation = (BunitNavigationManager)context.Services
            .GetRequiredService<NavigationManager>();
        _ = await rendered.WaitForElementAsync("[data-command='import']:not([disabled])");
        await ClickAndWaitForState(
            rendered,
            "create",
            () => !IsDisabled(rendered, "author"));
        var currentWorkspaceUri = navigation.Uri;

        rendered.FindComponent<InputFile>().UploadFiles(
            InputFileContent.CreateFromText(
                "not a package",
                "invalid.logiclab",
                contentType: "application/vnd.logiclab+zip"));

        await rendered.WaitForStateAsync(() => rendered.FindAll("[role='status']")
            .Any(status => status.TextContent.Contains(
                "package_invalid",
                StringComparison.Ordinal)));
        using (Assert.Multiple())
        {
            await Assert.That(navigation.Uri).IsEqualTo(currentWorkspaceUri);
            await Assert.That(IsDisabled(rendered, "create")).IsTrue();
            await Assert.That(IsDisabled(rendered, "author")).IsFalse();
            await Assert.That(IsDisabled(rendered, "export")).IsFalse();
        }
    }

    [Test]
    public async Task Editor_CreateSandbox_ReplacesLocatorAndReattachesWithPreservedFence()
    {
        await using var context = CreateContext();
        await using var workspace = new RecordingAttachmentWorkspace();
        var rendered = RenderEditor(context, workspace);
        var navigation = (BunitNavigationManager)context.Services
            .GetRequiredService<NavigationManager>();
        _ = await rendered.WaitForElementAsync("[data-command='create']:not([disabled])");

        await rendered.Find("[data-command='create']").ClickAsync();
        await rendered.WaitForStateAsync(() => workspace.Attachments.Count == 1);
        var firstAttachment = workspace.Attachments[0];
        var workspaceId = firstAttachment.Projection.WorkspaceId;
        await rendered.WaitForStateAsync(() => navigation.Uri.EndsWith(
            $"/editor/{workspaceId.Value}",
            StringComparison.Ordinal));

        var reloaded = context.Render<Editor>(parameters => parameters
            .Add(component => component.WorkspaceIdValue, workspaceId.Value));
        _ = await reloaded.WaitForElementAsync("[data-command='author']:not([disabled])");
        var request = (await Assert.That(workspace.AttachRequests.Last())
            .IsTypeOf<Reattach>())!;

        using (Assert.Multiple())
        {
            await Assert.That(navigation.History).Count().IsEqualTo(1);
            await Assert.That(navigation.History.Single().Options.ReplaceHistoryEntry)
                .IsTrue();
            await Assert.That(navigation.History.Single().Options.HistoryEntryState)
                .IsNotNull();
            await Assert.That(request.WorkspaceId).IsEqualTo(workspaceId);
            await Assert.That(request.PriorAttachmentId)
                .IsEqualTo(firstAttachment.AttachmentId);
            await Assert.That(request.PriorGeneration)
                .IsEqualTo(firstAttachment.Generation);
        }
    }

    [Test]
    public async Task Editor_AuthenticatedSandbox_ClaimsChangesAndSavesDurableProject()
    {
        await using var context = CreateContext();
        var repository = new InMemoryDurableProjectRepository();
        await using var workspace = new DurableWorkflowWorkspace(repository);
        var rendered = RenderAuthenticatedEditor(context, workspace);
        _ = await rendered.WaitForElementAsync("[data-command='create']:not([disabled])");
        await ClickAndWaitForState(
            rendered,
            "create",
            () => !IsDisabled(rendered, "claim"));

        await rendered.Find("[data-claim-name]").InputAsync(
            new ChangeEventArgs { Value = "Saved circuit" });
        await ClickAndWaitForState(
            rendered,
            "claim",
            () => repository.ClaimCallCount == 1 && !IsDisabled(rendered, "author"));
        await ClickAndWaitForState(
            rendered,
            "author",
            () => !IsDisabled(rendered, "save"));
        await ClickAndWaitForState(
            rendered,
            "save",
            () => repository.SaveCallCount == 1 && IsDisabled(rendered, "save"));

        using (Assert.Multiple())
        {
            await Assert.That(repository.LastClaim?.DisplayName.Value)
                .IsEqualTo("Saved circuit");
            await Assert.That(rendered.FindAll("[role='status']").Any(status =>
                    status.TextContent.Contains("saved", StringComparison.OrdinalIgnoreCase)))
                .IsTrue();
        }
    }

    [Test]
    public async Task Editor_SaveConflict_OffersReloadCopyAndExportRecovery()
    {
        await using var context = CreateContext();
        var repository = new InMemoryDurableProjectRepository
        {
            ConflictOnSave = true,
        };
        await using var workspace = new DurableWorkflowWorkspace(repository);
        var rendered = RenderAuthenticatedEditor(context, workspace);
        _ = await rendered.WaitForElementAsync("[data-command='create']:not([disabled])");
        await ClickAndWaitForState(
            rendered,
            "create",
            () => !IsDisabled(rendered, "claim"));
        await ClickAndWaitForState(
            rendered,
            "claim",
            () => !IsDisabled(rendered, "author"));
        await ClickAndWaitForState(
            rendered,
            "author",
            () => !IsDisabled(rendered, "save"));
        await rendered.Find("[data-command='save']").ClickAsync();
        var recovery = await rendered.WaitForElementAsync("[data-save-conflict]");
        await recovery.QuerySelector("[data-conflict-recovery='copy']")!
            .ClickAsync();
        await rendered.WaitForStateAsync(() => workspace.OpenRequests
            .Any(static request => request is CopyWorkspace));
        var navigation = (BunitNavigationManager)context.Services
            .GetRequiredService<NavigationManager>();

        using (Assert.Multiple())
        {
            await Assert.That(recovery.QuerySelector("[data-conflict-recovery='reload']"))
                .IsNotNull();
            await Assert.That(recovery.QuerySelector("[data-conflict-recovery='copy']"))
                .IsNotNull();
            await Assert.That(recovery.QuerySelector("[data-conflict-recovery='export']"))
                .IsNotNull();
            await Assert.That(IsDisabled(rendered, "save")).IsTrue();
            await Assert.That(workspace.OpenRequests.Last()).IsTypeOf<CopyWorkspace>();
            await Assert.That(navigation.History.Last().Options.ForceLoad).IsTrue();
            await Assert.That(navigation.History.Last().Options.ReplaceHistoryEntry)
                .IsTrue();
        }
    }

    [Test]
    public async Task Editor_ConfiguredPackagePolicyBoundsImport()
    {
        await using var context = CreateContext();
        await using var workspace = new PassthroughWorkspace();
        var package = await CreatePackageAsync();
        var limits = PackagePolicy.Development.Limits.ToArray();
        limits[(int)PackageDimension.CarrierBytes] = new PackageLimit(
            PackageDimension.CarrierBytes,
            checked((ulong)package.Length - 1));
        context.Services.AddSingleton(new PackagePolicy(
            "web-import-test",
            "1",
            limits));
        var rendered = RenderEditor(context, workspace);
        var navigation = (BunitNavigationManager)context.Services
            .GetRequiredService<NavigationManager>();
        _ = await rendered.WaitForElementAsync("[data-command='import']:not([disabled])");

        rendered.FindComponent<InputFile>().UploadFiles(
            InputFileContent.CreateFromBinary(
                package,
                "project.logiclab",
                contentType: "application/vnd.logiclab+zip"));

        await rendered.WaitForStateAsync(() => rendered.FindAll("[role='status']")
            .Any(status => status.TextContent.Contains(
                "package_limit_exceeded",
                StringComparison.Ordinal)));
        await Assert.That(navigation.Uri).IsEqualTo("http://localhost/");
    }

    [Test]
    public async Task Editor_PrepareExport_ProjectsOneTimeDownloadLink()
    {
        await using var context = CreateContext();
        await using var workspace = new PreparedExportWorkspace();
        var browserId = new AnonymousBrowserId(new string('a', 64));
        context.Services.AddSingleton<IEditorWorkspace>(workspace);
        context.Renderer.SetRendererInfo(new RendererInfo("Server", isInteractive: true));
        var authenticationState = Task.FromResult(new AuthenticationState(
            new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(
                    WorkspaceCallerAdapter.AnonymousBrowserClaimType,
                    browserId.Value)]))));
        var host = context.Render<CascadingValue<Task<AuthenticationState>>>(
            parameters => parameters
                .Add(value => value.Value, authenticationState)
                .AddChildContent<Editor>());
        var rendered = host.FindComponent<Editor>();
        _ = await rendered.WaitForElementAsync("[data-command='create']:not([disabled])");
        await ClickAndWaitForState(
            rendered,
            "create",
            () => !IsDisabled(rendered, "export"));

        await ClickAndWaitForState(
            rendered,
            "export",
            () => rendered.FindAll("[data-export-download]").Count == 1);

        var link = rendered.Find("[data-export-download]");
        var command = (await Assert.That(workspace.Command).IsTypeOf<PrepareExport>())!;
        using (Assert.Multiple())
        {
            await Assert.That(command.ProjectRevisionId)
                .IsEqualTo(command.Precondition.ProjectRevisionId);
            await Assert.That(command.Context.Caller)
                .IsEqualTo(new AnonymousBrowserWorkspaceCaller(browserId));
            await Assert.That(link.GetAttribute("href"))
                .IsEqualTo("/downloads/export-ticket-component-0001");
            await Assert.That(link.GetAttribute("download"))
                .IsEqualTo("logiclab-project.logiclab");
        }
    }

    [Test, Timeout(30_000)]
    public async Task Editor_PendingCompilation_DisposalCancelsWait(
        CancellationToken cancellationToken)
    {
        await using var context = CreateContext();
        await using var workspace = new BlockingCompilationObservationWorkspace();
        var rendered = await RenderAuthoredEditor(context, workspace);

        var compilation = rendered.Find("[data-command='compile']").ClickAsync();
        try
        {
            await workspace.ObservationStarted.WaitAsync(cancellationToken);
            await rendered.Instance.DisposeAsync();
            await Assert.That(workspace.ObservationCancellationToken.IsCancellationRequested)
                .IsTrue();
            await compilation.WaitAsync(cancellationToken);
        }
        finally
        {
            workspace.ReleaseObservation();
            await compilation.WaitAsync(cancellationToken);
        }
    }

    [Test, Timeout(30_000)]
    public async Task Editor_DisposalDuringTypedCancelledObservation_DetachesCapturedAttachment(
        CancellationToken cancellationToken)
    {
        await using var context = CreateContext();
        await using var workspace = new TypedCancellationCompilationObservationWorkspace();
        var rendered = await RenderAuthoredEditor(context, workspace);

        var compilation = rendered.Find("[data-command='compile']").ClickAsync();
        Task disposal = Task.CompletedTask;
        try
        {
            await workspace.ObservationStarted.WaitAsync(cancellationToken);
            disposal = rendered.Instance.DisposeAsync().AsTask();
            await workspace.CancellationStarted.WaitAsync(cancellationToken);
            workspace.ReleaseObservation();
            await compilation.WaitAsync(cancellationToken);
            workspace.AllowCancellationToComplete();
            await disposal.WaitAsync(cancellationToken);

            await Assert.That(workspace.DetachCount).IsEqualTo(1);
        }
        finally
        {
            workspace.AllowCancellationToComplete();
            workspace.ReleaseObservation();
            await compilation.WaitAsync(cancellationToken);
            await disposal.WaitAsync(cancellationToken);
        }
    }

    [Test, Timeout(30_000)]
    public async Task Editor_AcceptedCompilation_DisposalCancelsObservationOnly(
        CancellationToken cancellationToken)
    {
        await using var context = CreateContext();
        await using var workspace = new BlockingCompilationWorkspace();
        var rendered = await RenderAuthoredEditor(context, workspace);

        var compilation = rendered.Find("[data-command='compile']").ClickAsync();
        try
        {
            await workspace.CompilationDispatchStarted.WaitAsync(cancellationToken);
            await rendered.Instance.DisposeAsync();

            await Assert.That(workspace.CompilationCancellationToken.IsCancellationRequested)
                .IsFalse();
        }
        finally
        {
            workspace.AcceptCompilation();
            await compilation.WaitAsync(cancellationToken);
        }
    }

    [Test]
    public async Task Editor_StaticPrerender_RendersStableShellWithoutWorkspaceSideEffects()
    {
        await using var context = CreateContext();
        await using var workspace = new TrackingWorkspace();
        var rendered = RenderEditor(context, workspace, isInteractive: false);

        using (Assert.Multiple())
        {
            await Assert.That(rendered.FindAll("[data-component]")).IsEmpty();
            await Assert.That(workspace.OpenCount).IsEqualTo(0);
            await Assert.That(workspace.DispatchCount).IsEqualTo(0);
            await Assert.That(workspace.ReadCount).IsEqualTo(0);
            await Assert.That(AreAllCommandsDisabled(rendered)).IsTrue();
        }
    }

    [Test]
    public async Task Editor_InteractiveWorkspace_DisposalDetachesAttachment()
    {
        var context = CreateContext();
        await using var workspace = new TrackingWorkspace();
        var rendered = RenderEditor(context, workspace);
        _ = await rendered.WaitForElementAsync("[data-command='create']:not([disabled])");
        await ClickAndWaitForState(
            rendered,
            "create",
            () => !IsDisabled(rendered, "author"));

        await context.DisposeAsync();

        using (Assert.Multiple())
        {
            await Assert.That(workspace.AttachCount).IsEqualTo(1);
            await Assert.That(workspace.DetachCount).IsEqualTo(1);
        }
    }

    [Test]
    public async Task Editor_IdempotencyWindowCloses_ReattachesAndCompletesCommand()
    {
        await using var context = CreateContext();
        await using var workspace = new TrackingWorkspace(new WorkspacePolicy(
            policyId: "test-workspace",
            policyRevision: "1",
            globalWorkspaceLimit: 16,
            anonymousWorkspaceLimit: 16,
            workspaceCountPerSubject: 16,
            sandboxRetention: TimeSpan.FromHours(1),
            authoringLimits: WorkspaceAuthoringLimits.Default,
            historyRevisionCount: 16,
            idempotencyRecordCount: 1,
            detachedRetention: TimeSpan.FromMinutes(30),
            hotSwapPeakBytes: ulong.MaxValue,
            durableDisplayNameLimits: DurableDisplayNameLimits.Default,
            durableProjectCatalogLimits: DurableProjectCatalogLimits.Default));
        var rendered = RenderEditor(context, workspace);
        _ = await rendered.WaitForElementAsync("[data-command='create']:not([disabled])");

        await ClickAndWaitForState(
            rendered,
            "create",
            () => !IsDisabled(rendered, "author"));
        await ClickAndWaitForState(
            rendered,
            "author",
            () => !IsDisabled(rendered, "compile")
                && rendered.FindAll("[data-component]").Count == 3);

        await Assert.That(workspace.AttachCount).IsGreaterThan(1);
    }

    [Test]
    public async Task Editor_CompleteSimulationWorkflow_ProjectsProbeAndLogicalTime()
    {
        await using var context = CreateContext();
        await using var workspace = new TrackingWorkspace();
        var rendered = RenderEditor(context, workspace);
        _ = await rendered.WaitForElementAsync("[data-command='create']:not([disabled])");

        await ClickAndWaitForState(
            rendered,
            "create",
            () => !IsDisabled(rendered, "author"));
        await ClickAndWaitForState(
            rendered,
            "author",
            () => !IsDisabled(rendered, "compile")
                && rendered.FindAll("[data-component]").Count == 3);
        await ClickAndWaitForState(
            rendered,
            "compile",
            () => !IsDisabled(rendered, "session"));
        await ClickAndWaitForState(
            rendered,
            "session",
            () => !IsDisabled(rendered, "stimulus")
                && rendered.FindAll("[data-probe]").Count == 1);

        using (Assert.Multiple())
        {
            await Assert.That(rendered.Find("[data-probe] strong").TextContent)
                .IsEqualTo("1");
            await Assert.That(rendered.Find("[data-status='logical-time'] dd").TextContent)
                .IsEqualTo("0");
        }

        await ClickAndWaitForState(
            rendered,
            "stimulus",
            () => !IsDisabled(rendered, "step")
                && IsDisabled(rendered, "stimulus"));
        await ClickAndWaitForState(
            rendered,
            "step",
            () => rendered.Find("[data-status='logical-time'] dd").TextContent == "1"
                && rendered.Find("[data-probe] strong").TextContent == "0");

        using (Assert.Multiple())
        {
            await Assert.That(IsDisabled(rendered, "stimulus")).IsFalse();
            await Assert.That(IsDisabled(rendered, "step")).IsTrue();
        }
    }

    [Test]
    public async Task Editor_AdvanceFailure_RestoresInteractiveCommandState()
    {
        await using var context = CreateContext();
        await using var workspace = new FailingStepWorkspace();
        var rendered = RenderEditor(context, workspace);
        _ = await rendered.WaitForElementAsync("[data-command='create']:not([disabled])");
        await ClickAndWaitForState(
            rendered,
            "create",
            () => !IsDisabled(rendered, "author"));
        await ClickAndWaitForState(
            rendered,
            "author",
            () => !IsDisabled(rendered, "compile"));
        await ClickAndWaitForState(
            rendered,
            "compile",
            () => !IsDisabled(rendered, "session"));
        await ClickAndWaitForState(
            rendered,
            "session",
            () => !IsDisabled(rendered, "stimulus"));
        await ClickAndWaitForState(
            rendered,
            "stimulus",
            () => !IsDisabled(rendered, "step"));

        await ClickAndWaitForState(
            rendered,
            "step",
            () => !IsDisabled(rendered, "stimulus")
                && IsDisabled(rendered, "step"));

        using (Assert.Multiple())
        {
            await Assert.That(IsDisabled(rendered, "stimulus")).IsFalse();
            await Assert.That(IsDisabled(rendered, "step")).IsTrue();
        }
    }

    [Test]
    [Arguments("author-steering")]
    [Arguments("author-arithmetic")]
    public async Task Editor_GalleryWithoutProgrammableInputs_DisablesStimulus(
        string authorCommand)
    {
        await using var context = CreateContext();
        await using var workspace = new TrackingWorkspace();
        var rendered = RenderEditor(context, workspace);
        _ = await rendered.WaitForElementAsync("[data-command='create']:not([disabled])");

        await ClickAndWaitForState(
            rendered,
            "create",
            () => !IsDisabled(rendered, authorCommand));
        await ClickAndWaitForState(
            rendered,
            authorCommand,
            () => !IsDisabled(rendered, "compile"));

        await ClickAndWaitForState(
            rendered,
            "compile",
            () => !IsDisabled(rendered, "session"));
        await ClickAndWaitForState(
            rendered,
            "session",
            () => IsDisabled(rendered, "session")
                && IsDisabled(rendered, "stimulus"));

        await Assert.That(IsDisabled(rendered, "stimulus")).IsTrue();
    }

    [Test]
    public async Task Editor_ArithmeticGalleryWithMixedNetWidths_DisablesMergeCommand()
    {
        await using var context = CreateContext();
        await using var workspace = new TrackingWorkspace();
        var rendered = RenderEditor(context, workspace);
        _ = await rendered.WaitForElementAsync("[data-command='create']:not([disabled])");

        await ClickAndWaitForState(
            rendered,
            "create",
            () => !IsDisabled(rendered, "author-arithmetic"));
        await ClickAndWaitForState(
            rendered,
            "author-arithmetic",
            () => !IsDisabled(rendered, "compile"));

        await Assert.That(IsDisabled(rendered, "topology-merge")).IsTrue();
    }

    [Test]
    public async Task Editor_CreateWhileBusy_DisablesCommandsAndIgnoresSecondClick()
    {
        await using var context = CreateContext();
        await using var workspace = new BlockingWorkspace();
        var rendered = RenderEditor(context, workspace);
        await rendered.WaitForStateAsync(() => !IsDisabled(rendered, "create"));

        var firstClick = rendered.Find("[data-command='create']")
            .ClickAsync(new MouseEventArgs());
        await workspace.Started.WaitAsync(TimeSpan.FromSeconds(5));
        await rendered.WaitForStateAsync(() => IsDisabled(rendered, "author"));

        try
        {
            await rendered.Find("[data-command='compile']")
                .TriggerEventAsync("onclick", new MouseEventArgs());

            using (Assert.Multiple())
            {
                await Assert.That(workspace.OpenCount).IsEqualTo(1);
                await Assert.That(AreAllCommandsDisabled(rendered)).IsTrue();
            }
        }
        finally
        {
            workspace.Release();
        }

        await firstClick;
        await rendered.WaitForStateAsync(() => !IsDisabled(rendered, "author"));
        await Assert.That(workspace.OpenCount).IsEqualTo(1);
    }

    [Test]
    public async Task Editor_CreateAfterOpen_ReplayedDisabledCallback_DoesNotOpenSecondWorkspace()
    {
        await using var context = CreateContext();
        await using var workspace = new TrackingWorkspace();
        var rendered = RenderEditor(context, workspace);
        _ = await rendered.WaitForElementAsync("[data-command='create']:not([disabled])");

        await rendered.Find("[data-command='create']").ClickAsync(new MouseEventArgs());
        await rendered.WaitForStateAsync(() => !IsDisabled(rendered, "author"));
        var commandBar = rendered.FindComponent<WorkbenchCommandBar>();
        await rendered.InvokeAsync(() => commandBar.Instance.OnCreate.InvokeAsync());

        await Assert.That(workspace.OpenCount).IsEqualTo(1);
    }

    [Test]
    public async Task Editor_WorkspaceFailure_RemainsInteractiveAndAcceptsNextCommand()
    {
        await using var context = CreateContext();
        await using var workspace = new RecoveringWorkspace();
        var rendered = RenderEditor(context, workspace);
        _ = await rendered.WaitForElementAsync("[data-command='create']:not([disabled])");

        await rendered.Find("[data-command='create']")
            .ClickAsync(new MouseEventArgs());
        await rendered.WaitForStateAsync(() => !IsDisabled(rendered, "create"));

        using (Assert.Multiple())
        {
            await Assert.That(IsDisabled(rendered, "create")).IsFalse();
            await Assert.That(rendered.FindAll("[data-component]")).IsEmpty();
        }

        await rendered.Find("[data-command='create']")
            .ClickAsync(new MouseEventArgs());
        await rendered.WaitForStateAsync(() => !IsDisabled(rendered, "author"));

        using (Assert.Multiple())
        {
            await Assert.That(workspace.OpenCount).IsEqualTo(2);
            await Assert.That(IsDisabled(rendered, "author")).IsFalse();
        }
    }

    [Test]
    public async Task Editor_ExpiredWorkspaceCommand_ClearsStaleProjectionAndAcceptsNewSandbox()
    {
        await using var context = CreateContext();
        await using var workspace = new ExpiringWorkspace();
        var rendered = RenderEditor(context, workspace);
        _ = await rendered.WaitForElementAsync("[data-command='create']:not([disabled])");
        await rendered.Find("[data-command='create']").ClickAsync(new MouseEventArgs());
        await rendered.WaitForStateAsync(() => !IsDisabled(rendered, "author"));

        await rendered.Find("[data-command='author']").ClickAsync(new MouseEventArgs());
        await rendered.WaitForStateAsync(() => !IsDisabled(rendered, "create")
            && IsDisabled(rendered, "author")
            && rendered.FindAll("[data-component]").Count == 0);

        using (Assert.Multiple())
        {
            await Assert.That(IsDisabled(rendered, "create")).IsFalse();
            await Assert.That(IsDisabled(rendered, "author")).IsTrue();
            await Assert.That(rendered.FindAll("[data-component]")).IsEmpty();
        }

        await rendered.Find("[data-command='create']").ClickAsync(new MouseEventArgs());
        await rendered.WaitForStateAsync(() => !IsDisabled(rendered, "author"));

        using (Assert.Multiple())
        {
            await Assert.That(workspace.OpenCount).IsEqualTo(2);
            await Assert.That(IsDisabled(rendered, "author")).IsFalse();
        }
    }

    [Test]
    public async Task Editor_AuthorWhileBusy_KeepsNewlyAvailableCompileCommandDisabled()
    {
        await using var context = CreateContext();
        await using var workspace = new BlockingAuthorWorkspace();
        var rendered = RenderEditor(context, workspace);
        _ = await rendered.WaitForElementAsync("[data-command='create']:not([disabled])");
        await rendered.Find("[data-command='create']").ClickAsync(new MouseEventArgs());
        await rendered.WaitForStateAsync(() => !IsDisabled(rendered, "author"));

        var authoring = rendered.Find("[data-command='author']")
            .ClickAsync(new MouseEventArgs());
        await workspace.Started.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            await rendered.WaitForStateAsync(() => IsDisabled(rendered, "compile"));

            await rendered.Find("[data-command='compile']")
                .TriggerEventAsync("onclick", new MouseEventArgs());
            await Assert.That(workspace.DispatchCount).IsEqualTo(2);
        }
        finally
        {
            workspace.Release();
        }

        await authoring;
        await rendered.WaitForStateAsync(() => !IsDisabled(rendered, "compile"));
    }

    [Test]
    public async Task Editor_TopologyCommands_ExerciseCompleteUserEditingPath()
    {
        await using var context = CreateContext();
        await using var workspace = new TrackingWorkspace();
        var rendered = RenderEditor(context, workspace);
        _ = await rendered.WaitForElementAsync("[data-command='create']:not([disabled])");
        await ClickAndWaitForState(
            rendered,
            "create",
            () => !IsDisabled(rendered, "author"));
        await ClickAndWaitForState(
            rendered,
            "author",
            () => rendered.FindAll("[data-connection]").Count == 2);

        await ClickAndWaitForState(
            rendered,
            "topology-merge",
            () => rendered.FindAll("[data-connection]").Count == 1);

        await ClickAndWaitForState(
            rendered,
            "topology-split",
            () => rendered.FindAll("[data-connection]").Count == 2);

        await ClickAndWaitForState(
            rendered,
            "topology-add-junction",
            () => rendered.FindAll("[data-junction]").Count == 1);

        await ClickAndWaitForState(
            rendered,
            "topology-prepare-route",
            () => rendered.FindAll("[data-route-draft]").Count == 1);
        await Assert.That(rendered.FindAll("[data-wire-geometry]")).IsEmpty();

        await ClickAndWaitForState(
            rendered,
            "topology-commit-route",
            () => rendered.FindAll("[data-route-draft]").Count == 0
                && rendered.FindAll("[data-wire-geometry]").Count == 1);
        await Assert.That(await ReadSingleRoute(workspace))
            .IsTypeOf<OrthogonalWireRoute>();

        await rendered.Find("[data-command='topology-unroute']").ClickAsync();
        await Assert.That(await ReadSingleRoute(workspace))
            .IsTypeOf<UnroutedWireRoute>();

        await rendered.Find("[data-command='topology-route']").ClickAsync();
        await Assert.That(await ReadSingleRoute(workspace))
            .IsTypeOf<OrthogonalWireRoute>();

        await ClickAndWaitForState(
            rendered,
            "topology-remove-junction",
            () => rendered.FindAll("[data-junction]").Count == 0);

        await ClickAndWaitForState(
            rendered,
            "compile",
            () => !IsDisabled(rendered, "session"));
    }

    [Test]
    public async Task Editor_HierarchyCommands_NavigateDefinitionsAndCompileEntryOccurrence()
    {
        await using var context = CreateContext();
        await using var workspace = new TrackingWorkspace();
        var rendered = RenderEditor(context, workspace);
        _ = await rendered.WaitForElementAsync("[data-command='create']:not([disabled])");
        await ClickAndWaitForState(
            rendered,
            "create",
            () => !IsDisabled(rendered, "author-hierarchy"));
        await ClickAndWaitForState(
            rendered,
            "author-hierarchy",
            () => rendered.FindAll("[data-definition]").Count == 2);

        using (Assert.Multiple())
        {
            await Assert.That(rendered.FindAll("[data-definition]")).Count().IsEqualTo(2);
            await Assert.That(rendered.FindAll("[data-entry-marker]")).Count().IsEqualTo(1);
            await Assert.That(rendered.FindAll("[data-component]")).Count().IsEqualTo(3);
            await Assert.That(rendered.Find("[data-hierarchy-breadcrumb]").TextContent)
                .Contains("Main");
        }

        await rendered.Find("[data-enter-instance]").ClickAsync(new MouseEventArgs());
        await rendered.WaitForStateAsync(() => rendered.FindAll("[data-definition-port]").Count == 2);
        using (Assert.Multiple())
        {
            await Assert.That(rendered.Find("[data-hierarchy-breadcrumb]").TextContent)
                .Contains("Inverter");
            await Assert.That(rendered.FindAll("[data-component]")).Count().IsEqualTo(1);
            await Assert.That(rendered.FindAll("[data-connection]")).Count().IsEqualTo(2);
            await Assert.That(rendered.FindAll("[data-command='hierarchy-back']")).Count()
                .IsEqualTo(1);
        }

        await rendered.Find("[data-command='set-entry']").ClickAsync(new MouseEventArgs());
        await rendered.WaitForStateAsync(() => rendered.Find("[data-entry-marker]")
            .ParentElement!.TextContent.Contains("Inverter", StringComparison.Ordinal));
        var mainTab = rendered.FindAll("[data-definition]")
            .Single(element => element.TextContent.Contains("Main", StringComparison.Ordinal));
        await mainTab.ClickAsync(new MouseEventArgs());
        await rendered.WaitForStateAsync(() => rendered.FindAll("[data-enter-instance]").Count == 0);
        await rendered.Find("[data-command='set-entry']").ClickAsync(new MouseEventArgs());
        await rendered.WaitForStateAsync(() => rendered.Find("[data-entry-marker]")
            .ParentElement!.TextContent.Contains("Main", StringComparison.Ordinal));

        await rendered.Find("[data-enter-instance]").ClickAsync(new MouseEventArgs());
        await rendered.WaitForStateAsync(() => rendered.FindAll("[data-definition-port]").Count == 2);
        await rendered.Find("[data-command='hierarchy-back']")
            .ClickAsync(new MouseEventArgs());
        await rendered.WaitForStateAsync(() => rendered.FindAll("[data-enter-instance]").Count == 1);
        await ClickAndWaitForState(
            rendered,
            "compile",
            () => !IsDisabled(rendered, "session"));
        await ClickAndWaitForState(
            rendered,
            "session",
            () => rendered.FindAll("[data-probe]").Count == 1);

        await Assert.That(rendered.FindAll("[data-probe]")).Count().IsEqualTo(1);
    }

    [Test]
    public async Task Editor_CancelPreparedRoute_SendsNoCommandOrProjectRevision()
    {
        await using var context = CreateContext();
        await using var workspace = new TrackingWorkspace();
        var rendered = RenderEditor(context, workspace);
        _ = await rendered.WaitForElementAsync("[data-command='create']:not([disabled])");
        await ClickAndWaitForState(
            rendered,
            "create",
            () => !IsDisabled(rendered, "author"));
        await ClickAndWaitForState(
            rendered,
            "author",
            () => !IsDisabled(rendered, "topology-prepare-route"));
        var before = await workspace.ReadCurrent();
        var dispatchCount = workspace.DispatchCount;

        await rendered.Find("[data-command='topology-prepare-route']")
            .ClickAsync(new MouseEventArgs());
        await rendered.WaitForStateAsync(() => rendered.FindAll("[data-route-draft]").Count == 1);
        await rendered.Find("[data-command='topology-cancel-route']")
            .ClickAsync(new MouseEventArgs());
        await rendered.WaitForStateAsync(() => rendered.FindAll("[data-route-draft]").Count == 0);
        var after = await workspace.ReadCurrent();

        using (Assert.Multiple())
        {
            await Assert.That(workspace.DispatchCount).IsEqualTo(dispatchCount);
            await Assert.That(after.ProjectRevision.RevisionId)
                .IsEqualTo(before.ProjectRevision.RevisionId);
            await Assert.That(after.ProjectionVersion).IsEqualTo(before.ProjectionVersion);
            await Assert.That(after.ProjectRevision.Document.EntryCircuitDefinition.WireGeometries)
                .IsEmpty();
        }
    }

    private static BunitContext CreateContext()
    {
        var context = new BunitContext();
        context.JSInterop
            .SetupModule(
                "./_content/Microsoft.FluentUI.AspNetCore.Components/Components/InputFile/FluentInputFile.razor.js")
            .Mode = JSRuntimeMode.Loose;
        context.JSInterop
            .SetupModule(
                "./_content/Microsoft.FluentUI.AspNetCore.Components/Components/KeyCode/FluentKeyCode.razor.js")
            .Mode = JSRuntimeMode.Loose;
        context.Services.AddFluentUIComponents();
        context.Services.AddSingleton(TimeProvider.System);
        context.Services.AddSingleton(PackagePolicy.Development);
        context.Services.AddSingleton<ProjectImportWorkflow>();
        return context;
    }

    private static async Task<byte[]> CreatePackageAsync()
    {
        var revision = ((ProjectGenesisCommitted)ProjectEditor.Begin(
            new NewProjectSeed(
                "Uploaded project",
                LibrarySnapshot.Core,
                new SymbolProfileReference(
                    "TeachingMixed",
                    "1.0.0",
                    IndicationConvention.Negation),
                "Main"))).Revision;
        await using var carrier = new MemoryStream();
        var outcome = await ProjectPackage.WriteAsync(
            new ProjectPackageWriteRequest(
                revision,
                carrier,
                PackagePolicy.Development),
            CancellationToken.None);
        if (outcome is not PackageWriteSucceeded)
        {
            throw new InvalidOperationException("Test package write failed.");
        }

        return carrier.ToArray();
    }

    private static IRenderedComponent<Editor> RenderEditor(
        BunitContext context,
        IEditorWorkspace workspace,
        bool isInteractive = true)
    {
        context.Services.AddSingleton(workspace);
        context.Renderer.SetRendererInfo(new RendererInfo(
            isInteractive ? "Server" : "Static",
            isInteractive));
        return context.Render<Editor>();
    }

    private static IRenderedComponent<Editor> RenderAuthenticatedEditor(
        BunitContext context,
        IEditorWorkspace workspace)
    {
        context.Services.AddSingleton(workspace);
        context.Renderer.SetRendererInfo(new RendererInfo("Server", isInteractive: true));
        var authenticationState = Task.FromResult(new AuthenticationState(
            new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, "component-user"),
            ], "test"))));
        var host = context.Render<CascadingValue<Task<AuthenticationState>>>(parameters =>
            parameters
                .Add(value => value.Value, authenticationState)
                .AddChildContent<Editor>());
        return host.FindComponent<Editor>();
    }

    private static async Task<IRenderedComponent<Editor>> RenderAuthoredEditor(
        BunitContext context,
        IEditorWorkspace workspace)
    {
        var rendered = RenderEditor(context, workspace);
        _ = await rendered.WaitForElementAsync("[data-command='create']:not([disabled])");
        await ClickAndWaitForState(
            rendered,
            "create",
            () => !IsDisabled(rendered, "author"));
        await ClickAndWaitForState(
            rendered,
            "author",
            () => !IsDisabled(rendered, "compile"));
        return rendered;
    }

    private static async Task ClickAndWaitForState(
        IRenderedComponent<Editor> rendered,
        string command,
        Func<bool> statePredicate)
    {
        await rendered.Find($"[data-command='{command}']").ClickAsync();
        await rendered.WaitForStateAsync(statePredicate);
    }

    private static bool IsDisabled<TComponent>(
        IRenderedComponent<TComponent> rendered,
        string command)
        where TComponent : IComponent
    {
        return rendered.Find($"[data-command='{command}']").HasAttribute("disabled");
    }

    private static bool AreAllCommandsDisabled(IRenderedComponent<Editor> rendered)
    {
        var commands = rendered.FindAll("[data-command]");
        return commands.Count > 0
            && commands.All(command => command.HasAttribute("disabled"));
    }

    private static async Task<WireRoute> ReadSingleRoute(TrackingWorkspace workspace)
    {
        var projection = await workspace.ReadCurrent();
        return projection.ProjectRevision.Document.EntryCircuitDefinition
            .WireGeometries.Single().Route;
    }

    private sealed class PassthroughWorkspace : DelegatingEditorWorkspace
    {
    }

    private sealed class RecordingAttachmentWorkspace : DelegatingEditorWorkspace
    {
        public List<AttachRequest> AttachRequests { get; } = [];

        public List<Attached> Attachments { get; } = [];

        public override async Task<WorkspaceAttachOutcome> AttachAsync(
            AttachRequest request,
            CancellationToken cancellationToken)
        {
            AttachRequests.Add(request);
            var outcome = await base.AttachAsync(request, cancellationToken);
            if (outcome is Attached attached)
            {
                Attachments.Add(attached);
            }

            return outcome;
        }
    }

    private sealed class DurableWorkflowWorkspace(IDurableProjectRepository repository)
        : DelegatingEditorWorkspace(durableProjectRepository: repository)
    {
        public List<OpenWorkspaceRequest> OpenRequests { get; } = [];

        public override Task<WorkspaceOpenOutcome> OpenAsync(
            OpenWorkspaceRequest request,
            CancellationToken cancellationToken)
        {
            OpenRequests.Add(request);
            return base.OpenAsync(request, cancellationToken);
        }
    }

    private sealed class InMemoryDurableProjectRepository : IDurableProjectRepository
    {
        private DurableVersion? currentVersion;

        public bool ConflictOnSave { get; init; }

        public int ClaimCallCount { get; private set; }

        public int SaveCallCount { get; private set; }

        public DurableProjectClaimRequest? LastClaim { get; private set; }

        public Task<DurableProjectClaimRepositoryOutcome> ClaimAsync(
            DurableProjectClaimRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ClaimCallCount++;
            LastClaim = request;
            currentVersion = request.InitialDurableVersion;
            return Task.FromResult<DurableProjectClaimRepositoryOutcome>(
                new DurableProjectClaimStored(
                    request.DurableProjectId,
                    request.InitialDurableVersion,
                    request.ProjectRevision.RevisionId,
                    request.DisplayName));
        }

        public Task<DurableProjectClaimRepositoryOutcome?> TryReadClaimReceiptAsync(
            DurableProjectClaimRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<DurableProjectClaimRepositoryOutcome?>(null);
        }

        public Task<DurableProjectSaveRepositoryOutcome> SaveAsync(
            DurableProjectSaveRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SaveCallCount++;
            if (ConflictOnSave)
            {
                return Task.FromResult<DurableProjectSaveRepositoryOutcome>(
                    new DurableProjectSaveRepositoryConflict(
                        request.ExpectedDurableVersion,
                        request.NextDurableVersion));
            }

            if (request.ExpectedDurableVersion != currentVersion)
            {
                throw new InvalidOperationException("Unexpected durable version.");
            }

            currentVersion = request.NextDurableVersion;
            return Task.FromResult<DurableProjectSaveRepositoryOutcome>(
                new DurableProjectSaveStored(
                    request.NextDurableVersion,
                    request.ProjectRevision.RevisionId));
        }

        public Task<DurableProjectSaveRepositoryOutcome?> TryReadSaveReceiptAsync(
            DurableProjectSaveRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<DurableProjectSaveRepositoryOutcome?>(null);
        }
    }

    private sealed class BlockingWorkspace : DelegatingEditorWorkspace
    {
        private readonly TaskCompletionSource release = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource started = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int openCount;

        public Task Started => started.Task;

        public int OpenCount => Volatile.Read(ref openCount);

        public override async Task<WorkspaceOpenOutcome> OpenAsync(
            OpenWorkspaceRequest request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref openCount);
            started.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            return await base.OpenAsync(request, cancellationToken);
        }

        public void Release() => release.TrySetResult();
    }

    private sealed class PreparedExportWorkspace : DelegatingEditorWorkspace
    {
        public WorkspaceCommand? Command { get; private set; }

        public override Task<WorkspaceCommandOutcome> DispatchAsync(
            WorkspaceCommand command,
            CancellationToken cancellationToken)
        {
            if (command is not PrepareExport prepare)
            {
                return base.DispatchAsync(command, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            Command = command;
            return Task.FromResult<WorkspaceCommandOutcome>(new ExportPrepared(
                prepare.ProjectRevisionId,
                new ExportTicket("export-ticket-component-0001"),
                300));
        }
    }

    private sealed class BlockingCompilationObservationWorkspace : DelegatingEditorWorkspace
    {
        private readonly TaskCompletionSource observationStarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource releaseObservation = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private CompilationGeneration? acceptedGeneration;

        public CancellationToken ObservationCancellationToken { get; private set; }

        public Task ObservationStarted => observationStarted.Task;

        public void ReleaseObservation() => releaseObservation.TrySetResult();

        public override async Task<WorkspaceCommandOutcome> DispatchAsync(
            WorkspaceCommand command,
            CancellationToken cancellationToken)
        {
            var outcome = await base.DispatchAsync(command, cancellationToken);
            if (command is RequestCompilation && outcome is CompilationAccepted accepted)
            {
                acceptedGeneration = accepted.CompilationGeneration;
            }

            return outcome;
        }

        public override async Task<WorkspaceReadOutcome> ReadAsync(
            WorkspaceQueryContext context,
            WorkspaceQuery query,
            CancellationToken cancellationToken)
        {
            var outcome = await base.ReadAsync(context, query, cancellationToken);
            if (acceptedGeneration is not { } generation)
            {
                return outcome;
            }

            var projectionRead = query is ReadProjection
                ? (ProjectionSnapshot)outcome
                : (ProjectionSnapshot)await base.ReadAsync(
                    context,
                    ReadProjection.Instance,
                    cancellationToken);
            var projection = projectionRead.Projection;
            if (query is ReadCompilation)
            {
                ObservationCancellationToken = cancellationToken;
                observationStarted.TrySetResult();
                await releaseObservation.Task.WaitAsync(cancellationToken);
            }

            var compilation = new CompilationQueuedProjection(generation);
            return query is ReadCompilation
                ? new CompilationSnapshot(compilation, projection.ProjectionVersion)
                : new ProjectionSnapshot(projection with
                {
                    Compilation = compilation,
                });
        }
    }

    private sealed class TypedCancellationCompilationObservationWorkspace
        : DelegatingEditorWorkspace
    {
        private readonly TaskCompletionSource observationStarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource releaseObservation = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource cancellationStarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ManualResetEventSlim allowCancellationToComplete = new();
        private CompilationGeneration? acceptedGeneration;
        private int detachCount;

        public int DetachCount => Volatile.Read(ref detachCount);

        public Task CancellationStarted => cancellationStarted.Task;

        public Task ObservationStarted => observationStarted.Task;

        public void AllowCancellationToComplete() => allowCancellationToComplete.Set();

        public void ReleaseObservation() => releaseObservation.TrySetResult();

        public override async ValueTask DisposeAsync()
        {
            allowCancellationToComplete.Set();
            try
            {
                await base.DisposeAsync();
            }
            finally
            {
                allowCancellationToComplete.Dispose();
            }
        }

        public override async Task<WorkspaceCommandOutcome> DispatchAsync(
            WorkspaceCommand command,
            CancellationToken cancellationToken)
        {
            var outcome = await base.DispatchAsync(command, cancellationToken);
            if (command is RequestCompilation && outcome is CompilationAccepted accepted)
            {
                acceptedGeneration = accepted.CompilationGeneration;
            }

            return outcome;
        }

        public override async Task<WorkspaceReadOutcome> ReadAsync(
            WorkspaceQueryContext context,
            WorkspaceQuery query,
            CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return CancelledRead();
            }

            var outcome = await base.ReadAsync(context, query, cancellationToken);
            if (acceptedGeneration is not { } generation)
            {
                return outcome;
            }

            if (query is ReadCompilation)
            {
                observationStarted.TrySetResult();
                _ = cancellationToken.Register(() =>
                {
                    cancellationStarted.TrySetResult();
                    if (!allowCancellationToComplete.Wait(TimeSpan.FromSeconds(5)))
                    {
                        throw new InvalidOperationException(
                            "The test did not release observation cancellation.");
                    }
                });
                await releaseObservation.Task;

                return CancelledRead();
            }

            return outcome;
        }

        public override Task<WorkspaceDetachOutcome> DetachAsync(
            DetachRequest request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref detachCount);
            return base.DetachAsync(request, cancellationToken);
        }

        private static WorkspaceReadRejected CancelledRead()
        {
            return new WorkspaceReadRejected(
                "workspace_cancelled",
                [],
                RetryDisposition.RefreshProjection);
        }
    }

    private sealed class BlockingCompilationWorkspace : DelegatingEditorWorkspace
    {
        private readonly TaskCompletionSource compilationDispatchStarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource releaseCompilation = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public CancellationToken CompilationCancellationToken { get; private set; }

        public Task CompilationDispatchStarted => compilationDispatchStarted.Task;

        public void AcceptCompilation() => releaseCompilation.TrySetResult();

        public override async Task<WorkspaceCommandOutcome> DispatchAsync(
            WorkspaceCommand command,
            CancellationToken cancellationToken)
        {
            if (command is not RequestCompilation request)
            {
                return await base.DispatchAsync(command, cancellationToken);
            }

            CompilationCancellationToken = cancellationToken;
            compilationDispatchStarted.TrySetResult();
            await releaseCompilation.Task;
            return new CompilationAccepted(
                new CompilationGeneration(1),
                request.Precondition.ProjectRevisionId,
                1);
        }
    }

    private sealed class FailingStepWorkspace : DelegatingEditorWorkspace
    {
        public override Task<WorkspaceCommandOutcome> DispatchAsync(
            WorkspaceCommand command,
            CancellationToken cancellationToken)
        {
            return command is StepSession
                ? Task.FromResult<WorkspaceCommandOutcome>(new SessionAdvanceFailed(
                    sessionVersion: 1,
                    logicalTime: 0,
                    new AdvanceFailureProjection(
                        AdvanceFailureReason.SimulationInternalDefect,
                        [],
                        policyEvidence: null),
                    projectionVersion: 1))
                : base.DispatchAsync(command, cancellationToken);
        }
    }

    private sealed class RecoveringWorkspace : DelegatingEditorWorkspace
    {
        private int openCount;

        public int OpenCount => Volatile.Read(ref openCount);

        public override Task<WorkspaceOpenOutcome> OpenAsync(
            OpenWorkspaceRequest request,
            CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref openCount) == 1)
            {
                return Task.FromResult<WorkspaceOpenOutcome>(
                    new WorkspaceOpenRejected(
                        "workspace_internal_defect",
                        [],
                        RetryDisposition.DoNotRetry));
            }

            return base.OpenAsync(request, cancellationToken);
        }
    }

    private sealed class ExpiringWorkspace : DelegatingEditorWorkspace
    {
        private int isExpired;
        private int openCount;

        public int OpenCount => Volatile.Read(ref openCount);

        public override Task<WorkspaceOpenOutcome> OpenAsync(
            OpenWorkspaceRequest request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref openCount);
            Volatile.Write(ref isExpired, 0);
            return base.OpenAsync(request, cancellationToken);
        }

        public override Task<WorkspaceCommandOutcome> DispatchAsync(
            WorkspaceCommand command,
            CancellationToken cancellationToken)
        {
            Volatile.Write(ref isExpired, 1);
            return Task.FromResult<WorkspaceCommandOutcome>(
                new WorkspaceCommandRejected(
                    "workspace_expired",
                    [],
                    RetryDisposition.DoNotRetry));
        }

        public override Task<WorkspaceReadOutcome> ReadAsync(
            WorkspaceQueryContext context,
            WorkspaceQuery query,
            CancellationToken cancellationToken)
        {
            return Volatile.Read(ref isExpired) == 0
                ? base.ReadAsync(context, query, cancellationToken)
                : Task.FromResult<WorkspaceReadOutcome>(
                    new WorkspaceReadRejected(
                        "workspace_not_found",
                        [],
                        RetryDisposition.DoNotRetry));
        }
    }

    private sealed class BlockingAuthorWorkspace : DelegatingEditorWorkspace
    {
        private readonly TaskCompletionSource release = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource started = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int dispatchCount;

        public Task Started => started.Task;

        public int DispatchCount => Volatile.Read(ref dispatchCount);

        public override async Task<WorkspaceCommandOutcome> DispatchAsync(
            WorkspaceCommand command,
            CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref dispatchCount) == 2)
            {
                started.TrySetResult();
                await release.Task.WaitAsync(cancellationToken);
            }

            return await base.DispatchAsync(command, cancellationToken);
        }

        public void Release() => release.TrySetResult();
    }

    private sealed class TrackingWorkspace(WorkspacePolicy? workspacePolicy = null)
        : DelegatingEditorWorkspace(workspacePolicy)
    {
        private Attached? attachment;
        private int attachCount;
        private int detachCount;
        private WorkspaceId? workspaceId;
        private int dispatchCount;
        private int openCount;
        private int readCount;

        public int DispatchCount => Volatile.Read(ref dispatchCount);

        public int AttachCount => Volatile.Read(ref attachCount);

        public int DetachCount => Volatile.Read(ref detachCount);

        public int OpenCount => Volatile.Read(ref openCount);

        public int ReadCount => Volatile.Read(ref readCount);

        public override async Task<WorkspaceOpenOutcome> OpenAsync(
            OpenWorkspaceRequest request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref openCount);
            var outcome = await base.OpenAsync(request, cancellationToken);
            if (outcome is WorkspaceOpened opened)
            {
                workspaceId = opened.WorkspaceId;
            }

            return outcome;
        }

        public override Task<WorkspaceCommandOutcome> DispatchAsync(
            WorkspaceCommand command,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref dispatchCount);
            return base.DispatchAsync(command, cancellationToken);
        }

        public override async Task<WorkspaceAttachOutcome> AttachAsync(
            AttachRequest request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref attachCount);
            var outcome = await base.AttachAsync(request, cancellationToken);
            if (outcome is Attached attached)
            {
                attachment = attached;
            }

            return outcome;
        }

        public override async Task<WorkspaceDetachOutcome> DetachAsync(
            DetachRequest request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref detachCount);
            var outcome = await base.DetachAsync(request, cancellationToken);
            if (outcome is Detached)
            {
                attachment = null;
            }

            return outcome;
        }

        public override Task<WorkspaceReadOutcome> ReadAsync(
            WorkspaceQueryContext context,
            WorkspaceQuery query,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref readCount);
            return base.ReadAsync(context, query, cancellationToken);
        }

        public async Task<WorkspaceProjection> ReadCurrent()
        {
            var currentWorkspaceId = workspaceId
                ?? throw new InvalidOperationException("Workspace is not open.");
            var currentAttachment = attachment
                ?? throw new InvalidOperationException("Workspace is not attached.");
            var outcome = await base.ReadAsync(
                new WorkspaceQueryContext(
                    currentWorkspaceId,
                    currentAttachment.AttachmentId,
                    currentAttachment.Generation,
                    AnonymousWorkspaceCaller.Instance),
                ReadProjection.Instance,
                CancellationToken.None);
            return ((ProjectionSnapshot)outcome).Projection;
        }
    }
}
