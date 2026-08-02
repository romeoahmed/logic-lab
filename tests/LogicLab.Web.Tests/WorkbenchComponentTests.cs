using Bunit;
using LogicLab.Application.Workspaces;
using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.Presentation.Scene;
using LogicLab.Web.Components.Editor;
using LogicLab.Web.Components.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using TUnit.Assertions.Enums;

namespace LogicLab.Web.Tests;

public sealed class WorkbenchComponentTests
{
    [Test]
    public async Task WorkbenchCommandBar_EmptyProject_EnablesOnlyAuthoring()
    {
        using var context = CreateContext();

        var rendered = context.Render<WorkbenchCommandBar>(parameters => parameters
            .Add(component => component.CanAuthor, true));

        using (Assert.Multiple())
        {
            await Assert.That(IsDisabled(rendered, "create")).IsTrue();
            await Assert.That(IsDisabled(rendered, "author")).IsFalse();
            await Assert.That(IsDisabled(rendered, "compile")).IsTrue();
            await Assert.That(IsDisabled(rendered, "session")).IsTrue();
            await Assert.That(IsDisabled(rendered, "stimulus")).IsTrue();
            await Assert.That(IsDisabled(rendered, "step")).IsTrue();
        }
    }

    [Test]
    public async Task WorkbenchCommandBar_ActiveCommand_DisablesEveryCommand()
    {
        using var context = CreateContext();

        var rendered = context.Render<WorkbenchCommandBar>(parameters => parameters
            .Add(component => component.CanCompile, true)
            .Add(component => component.ActiveCommand, "compile"));

        foreach (var command in new[]
                 {
                     "create", "author", "compile", "session", "stimulus", "step",
                 })
        {
            await Assert.That(IsDisabled(rendered, command)).IsTrue();
        }
    }

    [Test]
    public async Task WorkbenchCommandBar_Commands_UseLabelledGroupSemantics()
    {
        using var context = CreateContext();

        var rendered = context.Render<WorkbenchCommandBar>();
        var group = rendered.Find("[role='group'][aria-label='Workbench commands']");

        using (Assert.Multiple())
        {
            await Assert.That(group.TagName).IsEqualTo("DIV");
            await Assert.That(rendered.FindAll("nav")).IsEmpty();
        }
    }

    [Test]
    public async Task TopologyCommandBar_Commands_UseLabelledGroupSemantics()
    {
        using var context = CreateContext();

        var rendered = context.Render<TopologyCommandBar>();
        var group = rendered.Find("[role='group'][aria-label='Topology commands']");

        using (Assert.Multiple())
        {
            await Assert.That(group.TagName).IsEqualTo("DIV");
            await Assert.That(rendered.FindAll("nav")).IsEmpty();
        }
    }

    [Test]
    public async Task Editor_CreateWhileBusy_DisablesCommandsAndIgnoresSecondClick()
    {
        using var context = CreateContext();
        await using var workspace = new BlockingWorkspace();
        context.Services.AddSingleton<IEditorWorkspace>(workspace);
        context.Renderer.SetRendererInfo(new RendererInfo("Server", isInteractive: true));
        var rendered = context.Render<Editor>();
        rendered.WaitForAssertion(() =>
        {
            if (IsDisabled(rendered, "create"))
            {
                throw new InvalidOperationException(
                    "Create must be enabled after interactivity.");
            }
        });

        var firstClick = rendered.Find("[data-command='create']")
            .ClickAsync(new MouseEventArgs());
        await workspace.Started.WaitAsync(TimeSpan.FromSeconds(5));
        rendered.WaitForAssertion(() =>
        {
            if (!IsDisabled(rendered, "author"))
            {
                throw new InvalidOperationException("Commands must be disabled while busy.");
            }
        });

        try
        {
            await rendered.Find("[data-command='compile']")
                .TriggerEventAsync("onclick", new MouseEventArgs());

            using (Assert.Multiple())
            {
                await Assert.That(workspace.OpenCount).IsEqualTo(1);
                foreach (var command in new[]
                         {
                             "create", "author", "compile", "session", "stimulus", "step",
                         })
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
        rendered.WaitForAssertion(() =>
        {
            if (IsDisabled(rendered, "author"))
            {
                throw new InvalidOperationException("Author must be restored after creation.");
            }
        });
        await Assert.That(workspace.OpenCount).IsEqualTo(1);
    }

    [Test]
    public async Task Editor_WorkspaceFailure_RemainsInteractiveAndAcceptsNextCommand()
    {
        using var context = CreateContext();
        await using var workspace = new RecoveringWorkspace();
        context.Services.AddSingleton<IEditorWorkspace>(workspace);
        context.Renderer.SetRendererInfo(new RendererInfo("Server", isInteractive: true));
        var rendered = context.Render<Editor>();
        rendered.WaitForElement("[data-command='create']:not([disabled])");

        await rendered.Find("[data-command='create']")
            .ClickAsync(new MouseEventArgs());

        using (Assert.Multiple())
        {
            await Assert.That(rendered.Find("[role='status']").TextContent)
                .Contains("workspace_internal_defect");
            await Assert.That(rendered.Markup).DoesNotContain("sensitive compiler detail");
            await Assert.That(IsDisabled(rendered, "create")).IsFalse();
        }

        await rendered.Find("[data-command='create']")
            .ClickAsync(new MouseEventArgs());

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
        using var context = CreateContext();
        await using var workspace = new ExpiringWorkspace();
        context.Services.AddSingleton<IEditorWorkspace>(workspace);
        context.Renderer.SetRendererInfo(new RendererInfo("Server", isInteractive: true));
        var rendered = context.Render<Editor>();
        rendered.WaitForElement("[data-command='create']:not([disabled])");
        await rendered.Find("[data-command='create']").ClickAsync(new MouseEventArgs());

        await rendered.Find("[data-command='author']").ClickAsync(new MouseEventArgs());

        using (Assert.Multiple())
        {
            await Assert.That(rendered.Find("[role='status']").TextContent)
                .Contains("workspace_expired");
            await Assert.That(IsDisabled(rendered, "create")).IsFalse();
            await Assert.That(IsDisabled(rendered, "author")).IsTrue();
        }

        await rendered.Find("[data-command='create']").ClickAsync(new MouseEventArgs());

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
        using var context = CreateContext();
        await using var workspace = new BlockingAuthorWorkspace();
        context.Services.AddSingleton<IEditorWorkspace>(workspace);
        context.Renderer.SetRendererInfo(new RendererInfo("Server", isInteractive: true));
        var rendered = context.Render<Editor>();
        rendered.WaitForElement("[data-command='create']:not([disabled])");
        await rendered.Find("[data-command='create']").ClickAsync(new MouseEventArgs());

        var authoring = rendered.Find("[data-command='author']")
            .ClickAsync(new MouseEventArgs());
        await workspace.Started.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            rendered.WaitForAssertion(() =>
            {
                if (!IsDisabled(rendered, "compile"))
                {
                    throw new InvalidOperationException(
                        "Compile must stay disabled while authoring.");
                }
            });

            await rendered.Find("[data-command='compile']")
                .TriggerEventAsync("onclick", new MouseEventArgs());
            await Assert.That(workspace.DispatchCount).IsEqualTo(2);
        }
        finally
        {
            workspace.Release();
        }

        await authoring;
        rendered.WaitForAssertion(() =>
        {
            if (IsDisabled(rendered, "compile"))
            {
                throw new InvalidOperationException("Compile must be restored after authoring.");
            }
        });
    }

    [Test]
    public async Task Editor_TopologyCommands_ExerciseCompleteUserEditingPath()
    {
        using var context = CreateContext();
        await using var workspace = new TrackingWorkspace();
        context.Services.AddSingleton<IEditorWorkspace>(workspace);
        context.Renderer.SetRendererInfo(new RendererInfo("Server", isInteractive: true));
        var rendered = context.Render<Editor>();
        rendered.WaitForElement("[data-command='create']:not([disabled])");
        await rendered.Find("[data-command='create']").ClickAsync(new MouseEventArgs());
        await rendered.Find("[data-command='author']").ClickAsync(new MouseEventArgs());

        await Assert.That(rendered.FindAll("[data-connection]")).Count().IsEqualTo(2);

        await rendered.Find("[data-command='topology-merge']")
            .ClickAsync(new MouseEventArgs());
        await Assert.That(rendered.FindAll("[data-connection]")).Count().IsEqualTo(1);

        await rendered.Find("[data-command='topology-split']")
            .ClickAsync(new MouseEventArgs());
        await Assert.That(rendered.FindAll("[data-connection]")).Count().IsEqualTo(2);

        await rendered.Find("[data-command='topology-add-junction']")
            .ClickAsync(new MouseEventArgs());
        await Assert.That(rendered.FindAll("[data-junction]")).Count().IsEqualTo(1);

        await rendered.Find("[data-command='topology-prepare-route']")
            .ClickAsync(new MouseEventArgs());
        using (Assert.Multiple())
        {
            await Assert.That(rendered.FindAll("[data-route-draft]")).Count().IsEqualTo(1);
            await Assert.That(rendered.FindAll("[data-wire-geometry]")).IsEmpty();
        }

        await rendered.Find("[data-command='topology-commit-route']")
            .ClickAsync(new MouseEventArgs());
        using (Assert.Multiple())
        {
            await Assert.That(rendered.FindAll("[data-route-draft]")).IsEmpty();
            await Assert.That(rendered.FindAll("[data-wire-geometry]")).Count().IsEqualTo(1);
            await Assert.That(rendered.Markup).Contains("Orthogonal");
        }

        await rendered.Find("[data-command='topology-unroute']")
            .ClickAsync(new MouseEventArgs());
        await Assert.That(rendered.Markup).Contains("Unrouted");

        await rendered.Find("[data-command='topology-route']")
            .ClickAsync(new MouseEventArgs());
        await Assert.That(rendered.Markup).Contains("Orthogonal");

        await rendered.Find("[data-command='topology-remove-junction']")
            .ClickAsync(new MouseEventArgs());
        await Assert.That(rendered.FindAll("[data-junction]")).IsEmpty();

        await rendered.Find("[data-command='compile']").ClickAsync(new MouseEventArgs());
        await Assert.That(rendered.Find("[role='status']").TextContent)
            .Contains("Compilation Artifact published");
    }

    [Test]
    public async Task Editor_CreateSampleTopologyPartitions_ComponentCreationOrder_DoesNotChangeElectricalPairs()
    {
        var revision = ((ProjectGenesisCommitted)ProjectEditor.Begin(new NewProjectSeed(
            "Reverse-order fixture",
            LibrarySnapshot.Core,
            new SymbolProfileReference(
                "TeachingMixed",
                "1.0.0",
                IndicationConvention.Negation),
            "Main"))).Revision;
        var definitionId = revision.Document.EntryCircuitDefinitionId;
        revision = Place(revision, "sink.output", [
            new ComponentParameterBinding("width", new Unsigned32ParameterValue(1)),
            new ComponentParameterBinding("radix", new ChoiceParameterValue("binary")),
        ], new GridPoint(8, 0));
        revision = Place(revision, "logic.not", [
            new ComponentParameterBinding("width", new Unsigned32ParameterValue(1)),
        ], new GridPoint(4, 0));
        revision = Place(revision, "source.input", [
            new ComponentParameterBinding("width", new Unsigned32ParameterValue(1)),
            new ComponentParameterBinding(
                "initialValue",
                new LogicVectorParameterValue([LogicValue.Zero])),
        ], new GridPoint(0, 0));
        var input = Find(revision, "source.input");
        var logicNot = Find(revision, "logic.not");
        var output = Find(revision, "sink.output");
        revision = Connect(revision,
            new InstanceTerminalReference(definitionId, input.Id, "Q"),
            new InstanceTerminalReference(definitionId, logicNot.Id, "A"));
        revision = Connect(revision,
            new InstanceTerminalReference(definitionId, logicNot.Id, "Q"),
            new InstanceTerminalReference(definitionId, output.Id, "D"));
        var beforeMerge = revision.Document.EntryCircuitDefinition;
        revision = Commit(ProjectEditor.Apply(
            revision,
            new MergeNetsIntent(
                definitionId,
                beforeMerge.Nets[0].Id,
                [beforeMerge.Nets[1].Id])));
        var definition = revision.Document.EntryCircuitDefinition;

        var partitions = Editor.CreateSampleTopologyPartitions(definition, definition.Nets.Single());
        var actualPairs = partitions
            .Select(partition => string.Join(
                "|",
                partition.Terminals
                    .Select(terminal =>
                        $"{definition.FindComponentInstance(terminal.ComponentInstanceId)!.ContractKey.ContractId}.{terminal.PortId}")
                    .Order(StringComparer.Ordinal)))
            .ToArray();

        await Assert.That(actualPairs).IsEquivalentTo(
            ["logic.not.A|source.input.Q", "logic.not.Q|sink.output.D"],
            CollectionOrdering.Matching);
    }

    [Test]
    public async Task Editor_CancelPreparedRoute_SendsNoCommandOrProjectRevision()
    {
        using var context = CreateContext();
        await using var workspace = new TrackingWorkspace();
        context.Services.AddSingleton<IEditorWorkspace>(workspace);
        context.Renderer.SetRendererInfo(new RendererInfo("Server", isInteractive: true));
        var rendered = context.Render<Editor>();
        rendered.WaitForElement("[data-command='create']:not([disabled])");
        await rendered.Find("[data-command='create']").ClickAsync(new MouseEventArgs());
        await rendered.Find("[data-command='author']").ClickAsync(new MouseEventArgs());
        var before = await workspace.ReadCurrent();
        var dispatchCount = workspace.DispatchCount;

        await rendered.Find("[data-command='topology-prepare-route']")
            .ClickAsync(new MouseEventArgs());
        await rendered.Find("[data-command='topology-cancel-route']")
            .ClickAsync(new MouseEventArgs());
        var after = await workspace.ReadCurrent();

        using (Assert.Multiple())
        {
            await Assert.That(workspace.DispatchCount).IsEqualTo(dispatchCount);
            await Assert.That(after.ProjectRevision.RevisionId)
                .IsEqualTo(before.ProjectRevision.RevisionId);
            await Assert.That(after.ProjectionVersion).IsEqualTo(before.ProjectionVersion);
            await Assert.That(after.ProjectRevision.Document.EntryCircuitDefinition.WireGeometries)
                .IsEmpty();
            await Assert.That(rendered.FindAll("[data-route-draft]")).IsEmpty();
            await Assert.That(rendered.Find("[role='status']").TextContent)
                .Contains("cancelled");
        }
    }

    [Test]
    public async Task AccessibleCircuitScene_CompleteCircuit_RendersReachableTopology()
    {
        using var context = CreateContext();
        var scene = AccessibleSceneProjector.Project(CreateCompleteCircuit());

        var rendered = context.Render<AccessibleCircuitScene>(parameters => parameters
            .Add(component => component.Scene, scene));

        using (Assert.Multiple())
        {
            await Assert.That(rendered.Find("section").GetAttribute("aria-labelledby"))
                .IsEqualTo("circuit-scene-heading");
            await Assert.That(rendered.FindAll("[data-component]")).Count().IsEqualTo(3);
            await Assert.That(rendered.FindAll("[data-connection]")).Count().IsEqualTo(2);
            await Assert.That(rendered.Markup).Contains("Input");
            await Assert.That(rendered.Markup).Contains("NOT");
            await Assert.That(rendered.Markup).Contains("Output");
            await Assert.That(rendered.Markup).Contains("Q → A");
            await Assert.That(rendered.Markup).Contains("Q → D");
        }
    }

    [Test]
    public async Task AccessibleCircuitScene_ExplicitTopology_RendersJunctionsAndRoutes()
    {
        using var context = CreateContext();
        var revision = CreateCompleteCircuit();
        var definition = revision.Document.EntryCircuitDefinition;
        var net = definition.Nets[0];
        revision = Commit(ProjectEditor.Apply(
            revision,
            new AddJunctionIntent(
                definition.Id,
                net.Id,
                new GridPoint(2, 1),
                [
                    new OrthogonalWireRoute(
                        [new GridPoint(0, 0), new GridPoint(0, 1), new GridPoint(4, 1)]),
                    new UnroutedWireRoute(),
                ],
                [],
                [])));
        var scene = AccessibleSceneProjector.Project(revision);

        var rendered = context.Render<AccessibleCircuitScene>(parameters => parameters
            .Add(component => component.Scene, scene));

        using (Assert.Multiple())
        {
            await Assert.That(rendered.FindAll("[data-junction]")).Count().IsEqualTo(1);
            await Assert.That(rendered.FindAll("[data-wire-geometry]")).Count().IsEqualTo(2);
            await Assert.That(rendered.Markup).Contains("Junction at grid 2, 1");
            await Assert.That(rendered.Markup).Contains("Orthogonal");
            await Assert.That(rendered.Markup).Contains("Unrouted");
        }
    }

    [Test]
    public async Task WorkbenchStatusStrip_StaticShell_ExposesIndependentStatusFacts()
    {
        using var context = CreateContext();

        var rendered = context.Render<WorkbenchStatusStrip>(parameters => parameters
            .Add(component => component.Message, "Connecting to the interactive workbench…"));

        using (Assert.Multiple())
        {
            await Assert.That(rendered.Find("[data-status='connection']").TextContent)
                .Contains("Connecting");
            await Assert.That(rendered.Find("[data-status='connection'] .status-dot")
                .ClassList).Contains("is-connecting");
            await Assert.That(rendered.Find("[data-status='logical-time']").TextContent)
                .Contains("—");
            await Assert.That(rendered.Find("[data-status='quiescence']").TextContent)
                .Contains("Unavailable");
            await Assert.That(rendered.Find("[data-status='trace']").TextContent)
                .Contains("Unavailable");
            await Assert.That(rendered.Find("[data-status='compilation']").TextContent)
                .Contains("Not requested");
            await Assert.That(rendered.Find("[data-status='save']").TextContent)
                .Contains("Sandbox");
        }
    }

    [Test]
    public async Task WorkbenchStatusStrip_InteractiveWithoutProject_ReportsConnected()
    {
        using var context = CreateContext();
        var rendered = context.Render<WorkbenchStatusStrip>(parameters => parameters
            .Add(component => component.IsConnected, true)
            .Add(component => component.Message, "Ready."));

        using (Assert.Multiple())
        {
            await Assert.That(rendered.Find("[data-status='connection']").TextContent)
                .Contains("Connected");
            await Assert.That(rendered.Find("[data-status='compilation']").TextContent)
                .Contains("Not requested");
            await Assert.That(rendered.Find("[data-status='logical-time']").TextContent)
                .Contains("—");
        }
    }

    private static BunitContext CreateContext()
    {
        var context = new BunitContext();
        context.Services.AddFluentUIComponents();
        return context;
    }

    private static bool IsDisabled<TComponent>(
        IRenderedComponent<TComponent> rendered,
        string command)
        where TComponent : IComponent
    {
        return rendered.Find($"[data-command='{command}']").HasAttribute("disabled");
    }

    private static ProjectRevision CreateCompleteCircuit()
    {
        var revision = ((ProjectGenesisCommitted)ProjectEditor.Begin(new NewProjectSeed(
            "Web fixture",
            LibrarySnapshot.Core,
            new SymbolProfileReference(
                "TeachingMixed",
                "1.0.0",
                IndicationConvention.Negation),
            "Main"))).Revision;
        var definitionId = revision.Document.EntryCircuitDefinitionId;
        revision = Place(revision, "source.input", [
            new ComponentParameterBinding("width", new Unsigned32ParameterValue(1)),
            new ComponentParameterBinding(
                "initialValue",
                new LogicVectorParameterValue([LogicValue.Zero])),
        ], new GridPoint(0, 0));
        var input = Find(revision, "source.input");
        revision = Place(revision, "logic.not", [
            new ComponentParameterBinding("width", new Unsigned32ParameterValue(1)),
        ], new GridPoint(4, 0));
        var logicNot = Find(revision, "logic.not");
        revision = Place(revision, "sink.output", [
            new ComponentParameterBinding("width", new Unsigned32ParameterValue(1)),
            new ComponentParameterBinding("radix", new ChoiceParameterValue("binary")),
        ], new GridPoint(8, 0));
        var output = Find(revision, "sink.output");
        revision = Connect(revision,
            new InstanceTerminalReference(definitionId, input.Id, "Q"),
            new InstanceTerminalReference(definitionId, logicNot.Id, "A"));
        return Connect(revision,
            new InstanceTerminalReference(definitionId, logicNot.Id, "Q"),
            new InstanceTerminalReference(definitionId, output.Id, "D"));
    }

    private static ProjectRevision Place(
        ProjectRevision revision,
        string contractId,
        ComponentParameterBinding[] parameters,
        GridPoint origin)
    {
        return Commit(ProjectEditor.Apply(
            revision,
            new PlaceComponentInstanceIntent(
                revision.Document.EntryCircuitDefinitionId,
                new ComponentContractKey(CoreLibrarySchema.LibraryId, contractId),
                parameters,
                new ComponentPlacement(origin))));
    }

    private static ProjectRevision Connect(
        ProjectRevision revision,
        params InstanceTerminalReference[] terminals)
    {
        return Commit(ProjectEditor.Apply(revision, new ConnectTerminalsIntent(terminals)));
    }

    private static ComponentInstance Find(ProjectRevision revision, string contractId)
    {
        return revision.Document.EntryCircuitDefinition.ComponentInstances
            .Single(instance => instance.ContractKey.ContractId == contractId);
    }

    private static ProjectRevision Commit(EditOutcome outcome)
    {
        return ((EditCommitted)outcome).Revision;
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

        public int DispatchCount => Volatile.Read(ref dispatchCount);

        public async Task<WorkspaceOpenOutcome> OpenAsync(
            OpenWorkspaceRequest request,
            CancellationToken cancellationToken)
        {
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
