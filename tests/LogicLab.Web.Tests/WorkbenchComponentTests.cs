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
using LogicLab.Web.Scene;
using LogicLab.Web.Transfers;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

namespace LogicLab.Web.Tests;

internal sealed partial class WorkbenchComponentTests
{
    [Test]
    public async Task Editor_SceneToolStrip_ChangesTheHostPrimaryTool()
    {
        await using var context = CreateContext();
        await using var workspace = new TrackingWorkspace();
        var rendered = await RenderAuthoredEditor(context, workspace);

        await rendered.Find("[data-scene-tool='wire']").ClickAsync();

        var sceneHost = rendered.FindComponent<CircuitSceneHost>();
        using (Assert.Multiple())
        {
            await Assert.That(sceneHost.Instance.ActiveTool)
                .IsTypeOf<SceneWireToolV1>();
            await Assert.That(rendered.Find("[data-scene-tool='probe']")
                    .HasAttribute("disabled"))
                .IsTrue();
        }
    }

    [Test]
    public async Task Editor_PlaceMemoryComponent_CreatesAndBindsAllXImageInOneEdit()
    {
        await using var context = CreateContext();
        await using var workspace = new TrackingWorkspace();
        var rendered = await RenderAuthoredEditor(context, workspace);
        var before = await workspace.ReadCurrent();
        var beforeComponentIds = before.ProjectRevision.Document.EntryCircuitDefinition
            .ComponentInstances.Select(component => component.Id).ToHashSet();
        var beforeDispatchCount = workspace.DispatchCount;

        await rendered.Find("[data-place-option='library:logiclab.core:memory.rom']")
            .ClickAsync();
        var sceneHost = rendered.FindComponent<CircuitSceneHost>();
        await rendered.WaitForStateAsync(() =>
            sceneHost.Instance.ActiveTool is ScenePlaceToolV1);
        var tool = (ScenePlaceToolV1)sceneHost.Instance.ActiveTool;

        await rendered.InvokeAsync(() => sceneHost.Instance.OnIntent.InvokeAsync(
            new PlaceComponentSceneIntentV1(
                LogicLabWebBuild.Fingerprint,
                sceneVersion: 1,
                before.ProjectionVersion,
                before.ProjectRevision.Document.EntryCircuitDefinitionId.Value,
                tool.Target,
                tool.Parameters,
                new SceneComponentPlacementV1(
                    new SceneGridPointV1(8, 4),
                    QuarterTurnsClockwise: 0,
                    Reflected: false),
                tool.DisplayName,
                "none")));
        await rendered.WaitForStateAsync(() => workspace.DispatchCount > beforeDispatchCount);

        var after = await workspace.ReadCurrent();
        var document = after.ProjectRevision.Document;
        var image = document.MemoryImages.Single();
        var component = document.EntryCircuitDefinition.ComponentInstances.Single(candidate =>
            !beforeComponentIds.Contains(candidate.Id));
        var imageBinding = (MemoryImageParameterValue)component.Parameters.Single(parameter =>
            string.Equals(parameter.ParameterId, "initialImage", StringComparison.Ordinal)).Value;

        using (Assert.Multiple())
        {
            await Assert.That(workspace.DispatchCount).IsEqualTo(beforeDispatchCount + 1);
            await Assert.That(imageBinding.MemoryImageId).IsEqualTo(image.Id);
            await Assert.That(image.Width).IsEqualTo(1u);
            await Assert.That(image.Depth).IsEqualTo(2u);
            await Assert.That(Enumerable.Range(0, checked((int)image.Depth)).All(address =>
                Enumerable.Range(0, checked((int)image.Width)).All(bit =>
                    image[(uint)address, (uint)bit] == LogicValue.X))).IsTrue();
        }
    }

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
    public async Task Editor_CreateSandbox_AtomicallyReplacesLocatorAndFenceWithoutNavigation()
    {
        await using var context = CreateContext(out var attachmentNavigation);
        await using var workspace = new RecordingAttachmentWorkspace();
        var rendered = RenderEditor(context, workspace);
        var navigation = (BunitNavigationManager)context.Services
            .GetRequiredService<NavigationManager>();
        _ = await rendered.WaitForElementAsync("[data-command='create']:not([disabled])");

        await rendered.Find("[data-command='create']").ClickAsync();
        await rendered.WaitForStateAsync(() => workspace.Attachments.Count == 1);
        var firstAttachment = workspace.Attachments[0];
        var workspaceId = firstAttachment.Projection.WorkspaceId;
        var invocation = attachmentNavigation.VerifyInvoke(
            "replaceHistoryEntry");
        var serializedFence = invocation.Arguments[1] as string;
        var hasFence = WorkspaceAttachmentHistoryState.TryRead(
            serializedFence,
            workspaceId,
            out var attachmentId,
            out var generation);

        using (Assert.Multiple())
        {
            await Assert.That(navigation.History).IsEmpty();
            await Assert.That(invocation.Arguments[0] as string)
                .IsEqualTo($"/editor/{workspaceId.Value}");
            await Assert.That(hasFence).IsTrue();
            await Assert.That(attachmentId)
                .IsEqualTo(firstAttachment.AttachmentId);
            await Assert.That(generation)
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

        await rendered.Find("[data-claim-name]").TriggerEventAsync(
            "ontextimmediate",
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

        await Assert.That(repository.LastClaim?.DisplayName.Value)
            .IsEqualTo("Saved circuit");
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
        var limits = PackagePolicy.Default.Limits.ToArray();
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
            await Assert.That(rendered.FindComponents<CircuitSceneHost>()).IsEmpty();
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
                && CurrentDefinition(rendered)?.ComponentInstances.Count == 3);

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
                && CurrentDefinition(rendered)?.ComponentInstances.Count == 3);
        await ClickAndWaitForState(
            rendered,
            "compile",
            () => !IsDisabled(rendered, "session"));
        await ClickAndWaitForState(
            rendered,
            "session",
            () => !IsDisabled(rendered, "stimulus")
                && rendered.FindAll(".probe-spine li").Count == 1);

        using (Assert.Multiple())
        {
            await Assert.That(rendered.Find(".probe-spine li strong").TextContent)
                .IsEqualTo("1");
            await Assert.That(rendered.Find("[data-status='logical-time'] dd").TextContent)
                .IsEqualTo("0");
        }

        await ClickAndWaitForState(
            rendered,
            "stimulus",
            () => !IsDisabled(rendered, "step"));
        await ClickAndWaitForState(
            rendered,
            "step",
            () => rendered.Find("[data-status='logical-time'] dd").TextContent == "1"
                && rendered.Find(".probe-spine li strong").TextContent == "0");

        using (Assert.Multiple())
        {
            await Assert.That(IsDisabled(rendered, "stimulus")).IsFalse();
            await Assert.That(IsDisabled(rendered, "step")).IsFalse();
        }

        var beforeToggle = await workspace.ReadCurrent();
        var definition = beforeToggle.ProjectRevision.Document.EntryCircuitDefinition;
        var boundProbe = beforeToggle.Simulation!.Probes.Single();
        var boundNet = (NetSourceIdentity)boundProbe.Source.Identity;
        var inputNet = definition.Nets.Single(net => net.Id != boundNet.NetId);
        var sceneHost = rendered.FindComponent<CircuitSceneHost>();
        await rendered.InvokeAsync(() => sceneHost.Instance.OnIntent.InvokeAsync(
            ToggleProbeIntent(beforeToggle, definition, inputNet.Id)));
        await rendered.WaitForStateAsync(() => rendered.FindAll(".probe-spine li").Count == 2);

        var beforeRemovingInput = await workspace.ReadCurrent();
        await rendered.InvokeAsync(() => sceneHost.Instance.OnIntent.InvokeAsync(
            ToggleProbeIntent(beforeRemovingInput, definition, inputNet.Id)));
        await rendered.WaitForStateAsync(() => rendered.FindAll(".probe-spine li").Count == 1);

        var beforeRemovingOutput = await workspace.ReadCurrent();
        await rendered.InvokeAsync(() => sceneHost.Instance.OnIntent.InvokeAsync(
            ToggleProbeIntent(beforeRemovingOutput, definition, boundNet.NetId)));
        await rendered.WaitForStateAsync(() => rendered.FindAll(".probe-spine li").Count == 0);
        var afterToggle = await workspace.ReadCurrent();

        await Assert.That(afterToggle.Simulation!.Probes).IsEmpty();
    }

    [Test]
    public async Task Editor_AutomaticClock_StepsWithoutSchedulingInput()
    {
        await using var context = CreateContext();
        await using var workspace = new TrackingWorkspace();
        var rendered = await RenderClockEditor(context, workspace);

        await Assert.That(IsDisabled(rendered, "step")).IsFalse();
        await rendered.Find("[data-command='step']").ClickAsync();
        var first = await workspace.ReadCurrent();
        await rendered.Find("[data-command='step']").ClickAsync();
        var second = await workspace.ReadCurrent();

        using (Assert.Multiple())
        {
            await Assert.That(first.Simulation!.LogicalTime).IsGreaterThan(0UL);
            await Assert.That(second.Simulation!.LogicalTime)
                .IsGreaterThan(first.Simulation.LogicalTime);
            await Assert.That(IsDisabled(rendered, "step")).IsFalse();
        }
    }

    [Test]
    [Arguments("restart")]
    [Arguments("close-session")]
    public async Task Editor_SessionLifecycleCommand_ClearsPendingStimulusAndUsesFreshProbeIds(
        string command)
    {
        await using var context = CreateContext();
        await using var workspace = new TrackingWorkspace();
        var rendered = await RenderSimulationEditor(context, workspace);
        var initial = await workspace.ReadCurrent();
        var definition = initial.ProjectRevision.Document.EntryCircuitDefinition;
        var originalProbe = initial.Simulation!.Probes.Single();
        var additionalNet = definition.Nets.Single(net =>
            net.Id != ((NetSourceIdentity)originalProbe.Source.Identity).NetId);
        var sceneHost = rendered.FindComponent<CircuitSceneHost>();
        await rendered.InvokeAsync(() => sceneHost.Instance.OnIntent.InvokeAsync(
            ToggleProbeIntent(initial, definition, additionalNet.Id)));
        var before = await workspace.ReadCurrent();
        await rendered.Find("[data-command='stimulus']").ClickAsync();

        await rendered.Find($"[data-command='{command}']").ClickAsync();
        if (command == "close-session")
        {
            await rendered.WaitForStateAsync(() => !IsDisabled(rendered, "session"));
            await ClickAndWaitForState(rendered, "session", () => !IsDisabled(rendered, "stimulus"));
        }
        else
        {
            await rendered.WaitForStateAsync(() => !IsDisabled(rendered, "stimulus"));
        }

        var after = await workspace.ReadCurrent();
        using (Assert.Multiple())
        {
            await Assert.That(after.Simulation!.SessionId).IsNotEqualTo(before.Simulation!.SessionId);
            await Assert.That(after.Simulation.LogicalTime).IsEqualTo(0UL);
            await Assert.That(after.Simulation.Probes.Any(probe =>
                before.Simulation.Probes.Any(previous => previous.ProbeId == probe.ProbeId)))
                .IsFalse();
            await Assert.That(after.Simulation.Probes).Count().IsEqualTo(command == "restart" ? 2 : 1);
            await Assert.That(IsDisabled(rendered, "step")).IsFalse();
            await Assert.That(IsDisabled(rendered, "stimulus")).IsFalse();
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Editor_ChangedCircuit_RecompilesBeforeRestartOrHotSwap(bool preserveState)
    {
        await using var context = CreateContext();
        await using var workspace = new TrackingWorkspace();
        var rendered = await RenderSimulationEditor(context, workspace);
        await ClickAndWaitForState(rendered, "stimulus", () => !IsDisabled(rendered, "step"));
        await ClickAndWaitForState(rendered, "step", () =>
            rendered.Find("[data-status='logical-time'] dd").TextContent == "1");
        var before = await workspace.ReadCurrent();
        var definition = before.ProjectRevision.Document.EntryCircuitDefinition;
        var component = definition.ComponentInstances[0];
        var sceneHost = rendered.FindComponent<CircuitSceneHost>();

        await rendered.InvokeAsync(() => sceneHost.Instance.OnIntent.InvokeAsync(
            new MoveComponentsSceneIntentV1(
                LogicLabWebBuild.Fingerprint, 1, before.ProjectionVersion, definition.Id.Value,
                [new SceneComponentMoveV1(
                    new SceneSourceRefV1(definition.Id.Value, "componentInstance", component.Id.Value),
                    new SceneComponentPlacementV1(new SceneGridPointV1(20, 12), 0, false))],
                "none")));
        await rendered.WaitForStateAsync(() => !IsDisabled(rendered, "compile"));
        using (Assert.Multiple())
        {
            await Assert.That(IsDisabled(rendered, "stimulus")).IsTrue();
            await Assert.That(IsDisabled(rendered, "restart")).IsTrue();
        }
        await ClickAndWaitForState(rendered, "compile", () => !IsDisabled(rendered, "hot-swap"));
        await rendered.Find($"[data-command='{(preserveState ? "hot-swap" : "restart")}']")
            .ClickAsync();
        await rendered.WaitForStateAsync(() => !IsDisabled(rendered, "stimulus"));
        var after = await workspace.ReadCurrent();

        using (Assert.Multiple())
        {
            await Assert.That(after.Simulation!.CompilationArtifactKey.ProjectRevisionId)
                .IsEqualTo(after.ProjectRevision.RevisionId);
            await Assert.That(after.Simulation.LogicalTime).IsEqualTo(preserveState ? 1UL : 0UL);
            await Assert.That(after.Simulation.SessionId == before.Simulation!.SessionId)
                .IsEqualTo(preserveState);
            await Assert.That(IsDisabled(rendered, "hot-swap")).IsTrue();
        }
    }

    private static async Task<IRenderedComponent<Editor>> RenderSimulationEditor(
        BunitContext context, TrackingWorkspace workspace)
    {
        var rendered = await RenderAuthoredEditor(context, workspace);
        await ClickAndWaitForState(rendered, "compile", () => !IsDisabled(rendered, "session"));
        await ClickAndWaitForState(rendered, "session", () => !IsDisabled(rendered, "stimulus"));
        return rendered;
    }

    private static ToggleProbeSceneIntentV1 ToggleProbeIntent(
        WorkspaceProjection projection,
        CircuitDefinition definition,
        NetId netId) => new(
            LogicLabWebBuild.Fingerprint,
            sceneVersion: 1,
            projection.ProjectionVersion,
            definition.Id.Value,
            new SceneElaboratedNetRefV1(
                new SceneSourceRefV1(
                    definition.Id.Value,
                    "net",
                    netId.Value),
                new SceneHierarchyPathV1(definition.Id.Value, [])));

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
            () => !IsDisabled(rendered, "stimulus"));

        using (Assert.Multiple())
        {
            await Assert.That(IsDisabled(rendered, "stimulus")).IsFalse();
            await Assert.That(IsDisabled(rendered, "step")).IsFalse();
        }
    }

    [Test]
    [Arguments("author-steering")]
    [Arguments("author-carry-lookahead")]
    [Arguments("author-bit-serial")]
    public async Task Editor_InteractiveStarter_CreatesRoutedCircuitAndEnablesStimulus(
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

        var definition = (await workspace.ReadCurrent())
            .ProjectRevision.Document.EntryCircuitDefinition;
        await Assert.That(definition.Nets).IsNotEmpty();
        await Assert.That(definition.Nets.All(net => definition.WireGeometries.Any(
                geometry => geometry.NetId == net.Id)))
            .IsTrue();
        await Assert.That(definition.WireGeometries.All(geometry =>
                geometry.Route is OrthogonalWireRoute { Points.Count: >= 2 }))
            .IsTrue();

        await ClickAndWaitForState(
            rendered,
            "compile",
            () => !IsDisabled(rendered, "session"));
        await ClickAndWaitForState(
            rendered,
            "session",
            () => IsDisabled(rendered, "session")
                && !IsDisabled(rendered, "stimulus"));
    }

    [Test]
    public async Task Editor_MixedWidthSelectedNets_DoesNotOfferMerge()
    {
        await using var context = CreateContext();
        await using var workspace = new TrackingWorkspace();
        var rendered = RenderEditor(context, workspace);
        _ = await rendered.WaitForElementAsync("[data-command='create']:not([disabled])");

        await ClickAndWaitForState(
            rendered,
            "create",
            () => !IsDisabled(rendered, "author-carry-lookahead"));
        await ClickAndWaitForState(
            rendered,
            "author-carry-lookahead",
            () => !IsDisabled(rendered, "compile"));

        var definition = CurrentDefinition(rendered)!;
        var nets = definition.Nets.DistinctBy(net => net.Width).Take(2).ToArray();
        await Select(rendered, [.. nets.Select(net => SceneSourceMap.From(new NetSourceIdentity(definition.Id, net.Id)))]);

        await Assert.That(rendered.FindAll("[data-command='selection-merge']")).IsEmpty();
        await Assert.That(rendered.FindAll("[data-selection-item]")).Count().IsEqualTo(2);
    }

    [Test, Timeout(30_000)]
    public async Task Editor_CreateWhileBusy_DisablesCommandsAndIgnoresSecondClick(
        CancellationToken cancellationToken)
    {
        await using var context = CreateContext();
        await using var workspace = new BlockingWorkspace();
        var rendered = RenderEditor(context, workspace);
        await rendered.WaitForStateAsync(() => !IsDisabled(rendered, "create"));

        var firstClick = rendered.Find("[data-command='create']")
            .ClickAsync(new MouseEventArgs());
        await workspace.Started.WaitAsync(cancellationToken);
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
        await rendered.InvokeAsync(() => commandBar.Instance.OnCommand.InvokeAsync(
            WorkbenchCommandBar.WorkbenchCommand.Create));

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
            await Assert.That(rendered.FindComponents<CircuitSceneHost>()).IsEmpty();
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
            && rendered.FindComponents<CircuitSceneHost>().Count == 0);

        await rendered.Find("[data-command='create']").ClickAsync(new MouseEventArgs());
        await rendered.WaitForStateAsync(() => !IsDisabled(rendered, "author"));

        using (Assert.Multiple())
        {
            await Assert.That(workspace.OpenCount).IsEqualTo(2);
            await Assert.That(IsDisabled(rendered, "author")).IsFalse();
        }
    }

    [Test, Timeout(30_000)]
    public async Task Editor_AuthorWhileBusy_KeepsNewlyAvailableCompileCommandDisabled(
        CancellationToken cancellationToken)
    {
        await using var context = CreateContext();
        await using var workspace = new BlockingAuthorWorkspace();
        var rendered = RenderEditor(context, workspace);
        _ = await rendered.WaitForElementAsync("[data-command='create']:not([disabled])");
        await rendered.Find("[data-command='create']").ClickAsync(new MouseEventArgs());
        await rendered.WaitForStateAsync(() => !IsDisabled(rendered, "author"));

        var authoring = rendered.Find("[data-command='author']")
            .ClickAsync(new MouseEventArgs());
        await workspace.Started.WaitAsync(cancellationToken);
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
    public async Task Editor_SelectionEdits_TargetOnlySelectedRouteWhileSessionIsPaused()
    {
        await using var context = CreateContext();
        await using var workspace = new TrackingWorkspace();
        var rendered = await RenderClockEditor(context, workspace);
        await ClickAndWaitForState(rendered, "run", () => !IsDisabled(rendered, "pause"));
        var before = await workspace.ReadCurrent();
        var definition = before.ProjectRevision.Document.EntryCircuitDefinition;
        var selected = definition.WireGeometries[^1];
        var untouched = definition.WireGeometries[0];
        await Select(rendered, [SceneSourceMap.From(new WireGeometrySourceIdentity(definition.Id, selected.Id))]);
        await Assert.That(IsDisabled(rendered, "selection-unroute")).IsTrue();
        await ClickAndWaitForState(rendered, "pause", () => !IsDisabled(rendered, "selection-unroute"));

        await ClickAndWaitForState(rendered, "selection-unroute", () =>
            CurrentDefinition(rendered)!.FindWireGeometry(selected.Id)!.Route is UnroutedWireRoute);
        var after = await workspace.ReadCurrent();
        using (Assert.Multiple())
        {
            await Assert.That(after.Simulation!.Run).IsTypeOf<RunPausedProjection>();
            await Assert.That(after.ProjectRevision.Document.EntryCircuitDefinition.FindWireGeometry(untouched.Id))
                .IsSameReferenceAs(untouched);
            await Assert.That(after.ProjectRevision.Document.EntryCircuitDefinition.Nets)
                .IsEquivalentTo(definition.Nets);
        }

        // A delayed click must not apply an intent built for the previous revision.
        var inspector = rendered.FindComponent<SelectionInspector>();
        var currentRevision = after.ProjectRevision.RevisionId;
        await rendered.InvokeAsync(() => inspector.Instance.OnEdit.InvokeAsync(new SelectionInspector.EditRequest(
            before.ProjectRevision.RevisionId, definition.Id,
            new RemoveWireGeometryIntent(definition.Id, untouched.Id))));
        await Assert.That((await workspace.ReadCurrent()).ProjectRevision.RevisionId).IsEqualTo(currentRevision);
    }

    private static Task Select(IRenderedComponent<Editor> rendered, IReadOnlyList<SceneSourceRefV1> sources) =>
        rendered.InvokeAsync(() => rendered.FindComponent<CircuitSceneHost>().Instance.OnSelect.InvokeAsync(
            new SceneSelectionV1(sources, "replace")));

    private static BunitContext CreateContext()
    {
        return CreateContext(out _);
    }

    private static BunitContext CreateContext(
        out BunitJSModuleInterop attachmentNavigation)
    {
        var context = WebTestContext.CreateBunitContext();
        context.JSInterop
            .SetupModule(
                "./_content/Microsoft.FluentUI.AspNetCore.Components/Components/InputFile/FluentInputFile.razor.js")
            .Mode = JSRuntimeMode.Loose;
        context.JSInterop
            .SetupModule(
                "./_content/Microsoft.FluentUI.AspNetCore.Components/Components/KeyCode/FluentKeyCode.razor.js")
            .Mode = JSRuntimeMode.Loose;
        attachmentNavigation = context.JSInterop.SetupModule(
            "./Components/Pages/Editor.razor.js");
        attachmentNavigation.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton(TimeProvider.System);
        context.Services.AddSingleton(PackagePolicy.Default);
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
                PackagePolicy.Default),
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

    private static CircuitDefinition? CurrentDefinition(
        IRenderedComponent<Editor> rendered)
    {
        var host = rendered.FindComponents<CircuitSceneHost>().SingleOrDefault()?.Instance;
        return host?.ProjectRevision.Document.FindCircuitDefinition(host.CircuitDefinitionId);
    }

    private static bool IsDisabled<TComponent>(
        IRenderedComponent<TComponent> rendered,
        string command)
        where TComponent : IComponent
    {
        var commands = rendered.FindAll($"[data-command='{command}']");
        return commands.Count == 0 || commands[0].HasAttribute("disabled");
    }

    private static bool AreAllCommandsDisabled(IRenderedComponent<Editor> rendered)
    {
        var commands = rendered.FindAll("[data-command]");
        return commands.Count > 0
            && commands.All(command => command.HasAttribute("disabled"));
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
                    allowCancellationToComplete.Wait();
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

    private class TrackingWorkspace(WorkspacePolicy? workspacePolicy = null)
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
