using Bunit;
using LogicLab.Application.Workspaces;
using LogicLab.Domain.Authoring;
using LogicLab.Engine.Compilation;
using LogicLab.Web.Components.Editor;
using LogicLab.Web.Components.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;

namespace LogicLab.Web.Tests;

internal sealed class WorkbenchComponentTests
{
    [Test, Timeout(30_000)]
    public async Task Editor_PendingCompilation_DisposalCancelsWait(
        CancellationToken cancellationToken)
    {
        var timeProvider = new ManualTimeProvider(
            new DateTimeOffset(2026, 8, 6, 0, 0, 0, TimeSpan.Zero));
        await using var context = CreateContext(timeProvider);
        await using var workspace = new ControlledCompilationWorkspace(
            blockFollowingRead: true);
        var rendered = await RenderAuthoredEditor(context, workspace);

        var compilation = rendered.Find("[data-command='compile']").ClickAsync();
        try
        {
            await workspace.FirstPendingRead.WaitAsync(cancellationToken);
            await timeProvider.TimerCreated.WaitAsync(cancellationToken);
            timeProvider.Advance(TimeSpan.FromMilliseconds(250));
            await workspace.BlockedRead.WaitAsync(cancellationToken);
            await rendered.Instance.DisposeAsync();
            await Assert.That(compilation).CompletesWithin(TimeSpan.FromSeconds(1));
        }
        finally
        {
            workspace.PublishAcceptedGeneration();
            await compilation.WaitAsync(cancellationToken);
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

    [Test, Timeout(30_000)]
    public async Task Editor_NewerCompilationGeneration_DoesNotReportAcceptedGenerationAsPublished(
        CancellationToken _)
    {
        await using var context = CreateContext();
        await using var workspace = new ControlledCompilationWorkspace(
            publishNewerGeneration: true);
        var rendered = await RenderAuthoredEditor(context, workspace);

        await rendered.Find("[data-command='compile']").ClickAsync();

        await Assert.That(rendered.Find("[role='status']").TextContent)
            .Contains("superseded");
    }

    [Test, Timeout(30_000)]
    public async Task Editor_PendingCompilation_UsesInjectedRefreshClock(
        CancellationToken cancellationToken)
    {
        var timeProvider = new ManualTimeProvider(
            new DateTimeOffset(2026, 8, 6, 0, 0, 0, TimeSpan.Zero));
        await using var context = CreateContext(timeProvider);
        await using var workspace = new ControlledCompilationWorkspace();
        var rendered = await RenderAuthoredEditor(context, workspace);

        var compilation = rendered.Find("[data-command='compile']").ClickAsync();
        await workspace.FirstPendingRead.WaitAsync(cancellationToken);
        await timeProvider.TimerCreated.WaitAsync(
            TimeSpan.FromSeconds(1),
            cancellationToken);
        workspace.PublishAcceptedGeneration();

        timeProvider.Advance(TimeSpan.FromMilliseconds(249));
        await Assert.That(compilation.IsCompleted).IsFalse();
        timeProvider.Advance(TimeSpan.FromMilliseconds(1));
        await compilation.WaitAsync(cancellationToken);

        await Assert.That(rendered.Find("[role='status']").TextContent)
            .Contains("published atomically");
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
            sandboxRetention: TimeSpan.FromHours(1),
            authoringLimits: WorkspaceAuthoringLimits.Default,
            historyRevisionCount: 16,
            idempotencyRecordCount: 1,
            detachedRetention: TimeSpan.FromMinutes(30),
            hotSwapPeakBytes: ulong.MaxValue));
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
    public async Task Editor_AdvanceFailure_ProjectsReasonWithoutInvalidCast()
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

        await rendered.Find("[data-command='step']").ClickAsync();

        await Assert.That(rendered.Find("[role='status']").TextContent)
            .Contains("simulation internal defect");
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
        await rendered.WaitForStateAsync(() => rendered.Find("[role='status']").TextContent
            .Contains("workspace_internal_defect", StringComparison.Ordinal));

        using (Assert.Multiple())
        {
            await Assert.That(rendered.Find("[role='status']").TextContent)
                .Contains("workspace_internal_defect");
            await Assert.That(rendered.Find("[role='status']").TextContent)
                .DoesNotContain("sensitive compiler detail");
            await Assert.That(IsDisabled(rendered, "create")).IsFalse();
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
        await rendered.WaitForStateAsync(() => rendered.Find("[role='status']").TextContent
            .Contains("workspace_expired", StringComparison.Ordinal));

        using (Assert.Multiple())
        {
            await Assert.That(rendered.Find("[role='status']").TextContent)
                .Contains("workspace_expired");
            await Assert.That(IsDisabled(rendered, "create")).IsFalse();
            await Assert.That(IsDisabled(rendered, "author")).IsTrue();
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
        await Assert.That(rendered.Find("[data-wire-geometry]").TextContent)
            .Contains("Orthogonal");

        await ClickAndWaitForState(
            rendered,
            "topology-unroute",
            () => rendered.Find("[data-wire-geometry]").TextContent
                .Contains("Unrouted", StringComparison.Ordinal));

        await ClickAndWaitForState(
            rendered,
            "topology-route",
            () => rendered.Find("[data-wire-geometry]").TextContent
                .Contains("Orthogonal", StringComparison.Ordinal));

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
        var childTerminalPaths = rendered
            .FindAll("[data-connection] .connection-summary span")
            .Select(element => element.TextContent)
            .ToArray();
        using (Assert.Multiple())
        {
            await Assert.That(rendered.Find("[data-hierarchy-breadcrumb]").TextContent)
                .Contains("Inverter");
            await Assert.That(rendered.FindAll("[data-component]")).Count().IsEqualTo(1);
            await Assert.That(childTerminalPaths).IsEquivalentTo(["A → A", "Q → Q"]);
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

    private static BunitContext CreateContext(TimeProvider? timeProvider = null)
    {
        var context = new BunitContext();
        context.Services.AddFluentUIComponents();
        context.Services.AddSingleton(timeProvider ?? TimeProvider.System);
        return context;
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

    private sealed class ControlledCompilationWorkspace(
        bool publishNewerGeneration = false,
        bool blockFollowingRead = false)
        : DelegatingEditorWorkspace
    {
        private readonly TaskCompletionSource blockedRead = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource firstPendingRead = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource releaseRead = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private CompilationGeneration? acceptedGeneration;
        private int pendingReadCount;
        private int publishAcceptedGeneration;

        public Task FirstPendingRead => firstPendingRead.Task;

        public Task BlockedRead => blockedRead.Task;

        public void PublishAcceptedGeneration()
        {
            Volatile.Write(ref publishAcceptedGeneration, 1);
            releaseRead.TrySetResult();
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
            CompilationProjection compilation;
            if (publishNewerGeneration)
            {
                var newer = new CompilationGeneration(checked(generation.Value + 1UL));
                compilation = query is ReadCompilation
                    ? new CompilationSupersededProjection(
                        generation,
                        newer)
                    : PublishedCompilation(projection, newer);
            }
            else if (Volatile.Read(ref publishAcceptedGeneration) != 0)
            {
                compilation = PublishedCompilation(projection, generation);
            }
            else
            {
                if (query is ReadCompilation)
                {
                    var readCount = Interlocked.Increment(ref pendingReadCount);
                    firstPendingRead.TrySetResult();
                    if (blockFollowingRead && readCount > 1)
                    {
                        blockedRead.TrySetResult();
                        await releaseRead.Task.WaitAsync(cancellationToken);
                    }
                }

                compilation = new CompilationQueuedProjection(generation);
            }

            return query is ReadCompilation
                ? new CompilationSnapshot(compilation, projection.ProjectionVersion)
                : new ProjectionSnapshot(projection with
                {
                    Compilation = compilation,
                });
        }

        private static CompilationPublishedProjection PublishedCompilation(
            WorkspaceProjection projection,
            CompilationGeneration generation)
        {
            var revision = projection.ProjectRevision;
            return new CompilationPublishedProjection(
                generation,
                new CompilationArtifactKey(
                    revision.RevisionId,
                    revision.Document.EntryCircuitDefinitionId,
                    revision.Document.LibrarySnapshot.Fingerprint,
                    "controlled-test"),
                []);
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
                    currentAttachment.Generation),
                ReadProjection.Instance,
                CancellationToken.None);
            return ((ProjectionSnapshot)outcome).Projection;
        }
    }
}
