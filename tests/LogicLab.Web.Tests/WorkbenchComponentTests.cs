using Bunit;
using LogicLab.Application.Workspaces;
using LogicLab.Domain.Authoring;
using LogicLab.Web.Components.Editor;
using LogicLab.Web.Components.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;

namespace LogicLab.Web.Tests;

internal sealed class WorkbenchComponentTests
{
    private static readonly string[] WorkbenchCommands =
    [
        "create",
        "author",
        "author-steering",
        "author-arithmetic",
        "author-hierarchy",
        "compile",
        "session",
        "stimulus",
        "step",
    ];

    private static readonly string[] SteeringComponentLabels =
    [
        "AND", "Buffer", "Decoder", "DEMUX", "MUX", "NAND", "NOR", "OR",
        "Priority Encoder", "Tri-State", "XNOR", "XOR",
    ];

    private static readonly string[] ArithmeticComponentLabels =
    [
        "Adder", "Logical Shift", "Subtractor", "Unsigned Compare",
    ];

    [Test]
    public async Task Editor_StaticPrerender_RendersStableShellWithoutWorkspaceSideEffects()
    {
        await using var context = CreateContext();
        await using var workspace = new TrackingWorkspace();
        var rendered = RenderEditor(context, workspace, isInteractive: false);

        using (Assert.Multiple())
        {
            await Assert.That(rendered.Find("h1").TextContent)
                .IsEqualTo("Sandbox Workbench");
            await Assert.That(rendered.Find("[role='status']").TextContent)
                .Contains("Connecting");
            await Assert.That(rendered.FindAll("[data-component]")).IsEmpty();
            await Assert.That(rendered.Find(".scene .empty-state").TextContent)
                .Contains("Create a Sandbox Project");
            await Assert.That(workspace.OpenCount).IsEqualTo(0);
            await Assert.That(workspace.DispatchCount).IsEqualTo(0);
            await Assert.That(workspace.ReadCount).IsEqualTo(0);
            foreach (var command in WorkbenchCommands)
            {
                await Assert.That(IsDisabled(rendered, command)).IsTrue();
            }
        }
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
            await Assert.That(rendered.Find("[role='status']").TextContent)
                .Contains("Step committed at Logical Time 1");
            await Assert.That(rendered.Find("[data-status='quiescence'] dd").TextContent)
                .IsEqualTo("Quiescent");
            await Assert.That(IsDisabled(rendered, "stimulus")).IsFalse();
            await Assert.That(IsDisabled(rendered, "step")).IsTrue();
        }
    }

    [Test]
    public async Task Editor_SteeringGallery_CreatesSessionWithoutOfferingUnavailableStimulus()
    {
        await using var context = CreateContext();
        await using var workspace = new TrackingWorkspace();
        var rendered = RenderEditor(context, workspace);
        _ = await rendered.WaitForElementAsync("[data-command='create']:not([disabled])");

        await ClickAndWaitForState(
            rendered,
            "create",
            () => !IsDisabled(rendered, "author-steering"));
        await ClickAndWaitForState(
            rendered,
            "author-steering",
            () => rendered.FindAll("[data-component] h3")
                .Any(element => element.TextContent == "Priority Encoder"));

        var labels = rendered.FindAll("[data-component] h3")
            .Select(element => element.TextContent)
            .ToArray();
        var steeringLabels = labels
            .Where(SteeringComponentLabels.Contains)
            .ToArray();
        var mux = rendered.FindAll("[data-component]").Single(element =>
            element.QuerySelector("h3")?.TextContent == "MUX");
        var priorityEncoder = rendered.FindAll("[data-component]").Single(element =>
            element.QuerySelector("h3")?.TextContent == "Priority Encoder");
        using (Assert.Multiple())
        {
            await Assert.That(steeringLabels).IsEquivalentTo(SteeringComponentLabels);
            await Assert.That(mux.TextContent).Contains("D0 · Input · 1 bit");
            await Assert.That(mux.TextContent).Contains("D1 · Input · 1 bit");
            await Assert.That(priorityEncoder.TextContent).Contains("A0 · Input · 1 bit");
            await Assert.That(priorityEncoder.TextContent).Contains("VALID · Output · 1 bit");
        }

        await ClickAndWaitForState(
            rendered,
            "compile",
            () => rendered.Find("[role='status']").TextContent
                .Contains("Compilation Artifact published", StringComparison.Ordinal));
        await ClickAndWaitForState(
            rendered,
            "session",
            () => IsDisabled(rendered, "stimulus")
                && rendered.Find("[role='status']").TextContent
                    .Contains("no programmable inputs", StringComparison.Ordinal));
    }

    [Test]
    public async Task Editor_ArithmeticGallery_CreatesSessionWithoutOfferingUnavailableStimulus()
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
            () => rendered.FindAll("[data-component] h3")
                .Any(element => element.TextContent == "Logical Shift"));

        var labels = rendered.FindAll("[data-component] h3")
            .Select(element => element.TextContent)
            .ToArray();
        var arithmeticLabels = labels
            .Where(ArithmeticComponentLabels.Contains)
            .ToArray();
        var shift = rendered.FindAll("[data-component]").Single(element =>
            element.QuerySelector("h3")?.TextContent == "Logical Shift");
        using (Assert.Multiple())
        {
            await Assert.That(arithmeticLabels).IsEquivalentTo(ArithmeticComponentLabels);
            await Assert.That(shift.TextContent).Contains("D · Input · 3 bit");
            await Assert.That(shift.TextContent).Contains("AMOUNT · Input · 2 bit");
            await Assert.That(shift.TextContent).Contains("Q · Output · 3 bit");
        }

        await ClickAndWaitForState(
            rendered,
            "compile",
            () => !IsDisabled(rendered, "session"));
        await ClickAndWaitForState(
            rendered,
            "session",
            () => IsDisabled(rendered, "stimulus")
                && rendered.Find("[role='status']").TextContent
                    .Contains("no programmable inputs", StringComparison.Ordinal));
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
                foreach (var command in WorkbenchCommands)
                {
                    await Assert.That(IsDisabled(rendered, command)).IsTrue();
                }
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
            await Assert.That(rendered.Markup).DoesNotContain("sensitive compiler detail");
            await Assert.That(IsDisabled(rendered, "create")).IsFalse();
        }

        await rendered.Find("[data-command='create']")
            .ClickAsync(new MouseEventArgs());
        await rendered.WaitForStateAsync(() => !IsDisabled(rendered, "author"));

        using (Assert.Multiple())
        {
            await Assert.That(workspace.OpenCount).IsEqualTo(2);
            await Assert.That(rendered.Find("[role='status']").TextContent)
                .Contains("Sandbox Project created");
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
            await Assert.That(rendered.Find("[role='status']").TextContent)
                .Contains("Sandbox Project created");
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
            () => rendered.Find("[role='status']").TextContent
                .Contains("Compilation Artifact published", StringComparison.Ordinal));
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

        using (Assert.Multiple())
        {
            await Assert.That(rendered.Find("[role='status']").TextContent)
                .Contains("Simulation Session created");
            await Assert.That(rendered.FindAll("[data-probe]")).Count().IsEqualTo(1);
        }
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
            await Assert.That(rendered.Find("[role='status']").TextContent)
                .Contains("cancelled");
        }
    }

    private static BunitContext CreateContext()
    {
        var context = new BunitContext();
        context.Services.AddFluentUIComponents();
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

    private sealed class BlockingWorkspace : IEditorWorkspace
    {
        private readonly IEditorWorkspace inner = EditorWorkspaceFactory.Create();
        private readonly TaskCompletionSource release = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource started = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int openCount;

        public Task Started => started.Task;

        public int OpenCount => Volatile.Read(ref openCount);

        public async Task<WorkspaceOpenOutcome> OpenAsync(
            OpenWorkspaceRequest request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref openCount);
            started.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            return await inner.OpenAsync(request, cancellationToken);
        }

        public Task<WorkspaceCommandOutcome> DispatchAsync(
            WorkspaceCommand command,
            CancellationToken cancellationToken)
        {
            return inner.DispatchAsync(command, cancellationToken);
        }

        public Task<WorkspaceReadOutcome> ReadAsync(
            WorkspaceId workspaceId,
            CancellationToken cancellationToken)
        {
            return inner.ReadAsync(workspaceId, cancellationToken);
        }

        public ValueTask DisposeAsync() => inner.DisposeAsync();

        public void Release() => release.TrySetResult();
    }

    private sealed class RecoveringWorkspace : IEditorWorkspace
    {
        private readonly IEditorWorkspace inner = EditorWorkspaceFactory.Create();
        private int openCount;

        public int OpenCount => Volatile.Read(ref openCount);

        public Task<WorkspaceOpenOutcome> OpenAsync(
            OpenWorkspaceRequest request,
            CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref openCount) == 1)
            {
                return Task.FromResult<WorkspaceOpenOutcome>(
                    new WorkspaceOpenRejected("workspace_internal_defect", []));
            }

            return inner.OpenAsync(request, cancellationToken);
        }

        public Task<WorkspaceCommandOutcome> DispatchAsync(
            WorkspaceCommand command,
            CancellationToken cancellationToken)
        {
            return inner.DispatchAsync(command, cancellationToken);
        }

        public Task<WorkspaceReadOutcome> ReadAsync(
            WorkspaceId workspaceId,
            CancellationToken cancellationToken)
        {
            return inner.ReadAsync(workspaceId, cancellationToken);
        }

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }

    private sealed class ExpiringWorkspace : IEditorWorkspace
    {
        private readonly IEditorWorkspace inner = EditorWorkspaceFactory.Create();
        private int isExpired;
        private int openCount;

        public int OpenCount => Volatile.Read(ref openCount);

        public Task<WorkspaceOpenOutcome> OpenAsync(
            OpenWorkspaceRequest request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref openCount);
            Volatile.Write(ref isExpired, 0);
            return inner.OpenAsync(request, cancellationToken);
        }

        public Task<WorkspaceCommandOutcome> DispatchAsync(
            WorkspaceCommand command,
            CancellationToken cancellationToken)
        {
            Volatile.Write(ref isExpired, 1);
            return Task.FromResult<WorkspaceCommandOutcome>(
                new WorkspaceCommandRejected("workspace_expired", []));
        }

        public Task<WorkspaceReadOutcome> ReadAsync(
            WorkspaceId workspaceId,
            CancellationToken cancellationToken)
        {
            return Volatile.Read(ref isExpired) == 0
                ? inner.ReadAsync(workspaceId, cancellationToken)
                : Task.FromResult<WorkspaceReadOutcome>(
                    new WorkspaceReadRejected("workspace_not_found"));
        }

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }

    private sealed class BlockingAuthorWorkspace : IEditorWorkspace
    {
        private readonly IEditorWorkspace inner = EditorWorkspaceFactory.Create();
        private readonly TaskCompletionSource release = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource started = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int dispatchCount;

        public Task Started => started.Task;

        public int DispatchCount => Volatile.Read(ref dispatchCount);

        public Task<WorkspaceOpenOutcome> OpenAsync(
            OpenWorkspaceRequest request,
            CancellationToken cancellationToken)
        {
            return inner.OpenAsync(request, cancellationToken);
        }

        public async Task<WorkspaceCommandOutcome> DispatchAsync(
            WorkspaceCommand command,
            CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref dispatchCount) == 2)
            {
                started.TrySetResult();
                await release.Task.WaitAsync(cancellationToken);
            }

            return await inner.DispatchAsync(command, cancellationToken);
        }

        public Task<WorkspaceReadOutcome> ReadAsync(
            WorkspaceId workspaceId,
            CancellationToken cancellationToken)
        {
            return inner.ReadAsync(workspaceId, cancellationToken);
        }

        public ValueTask DisposeAsync() => inner.DisposeAsync();

        public void Release() => release.TrySetResult();
    }

    private sealed class TrackingWorkspace : IEditorWorkspace
    {
        private readonly IEditorWorkspace inner = EditorWorkspaceFactory.Create();
        private WorkspaceId? workspaceId;
        private int dispatchCount;
        private int openCount;
        private int readCount;

        public int DispatchCount => Volatile.Read(ref dispatchCount);

        public int OpenCount => Volatile.Read(ref openCount);

        public int ReadCount => Volatile.Read(ref readCount);

        public async Task<WorkspaceOpenOutcome> OpenAsync(
            OpenWorkspaceRequest request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref openCount);
            var outcome = await inner.OpenAsync(request, cancellationToken);
            if (outcome is WorkspaceOpened opened)
            {
                workspaceId = opened.WorkspaceId;
            }

            return outcome;
        }

        public Task<WorkspaceCommandOutcome> DispatchAsync(
            WorkspaceCommand command,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref dispatchCount);
            return inner.DispatchAsync(command, cancellationToken);
        }

        public Task<WorkspaceReadOutcome> ReadAsync(
            WorkspaceId requestedWorkspaceId,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref readCount);
            return inner.ReadAsync(requestedWorkspaceId, cancellationToken);
        }

        public async Task<WorkspaceProjection> ReadCurrent()
        {
            var outcome = await inner.ReadAsync(
                workspaceId ?? throw new InvalidOperationException("Workspace is not open."),
                CancellationToken.None);
            return ((ProjectionSnapshot)outcome).Projection;
        }

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }

}
