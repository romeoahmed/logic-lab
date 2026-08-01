using Bunit;
using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.Presentation.Scene;
using LogicLab.Web.Components.Editor;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;

namespace LogicLab.Web.Tests;

public sealed class WorkbenchComponentTests
{
    [Test]
    public async Task WorkbenchCommandBar_EmptyProject_EnablesOnlyAuthoring()
    {
        using var context = CreateContext();

        var rendered = context.Render<WorkbenchCommandBar>(parameters => parameters
            .Add(component => component.State, WorkbenchViewState.EmptyProject));

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
            .Add(component => component.State, WorkbenchViewState.CircuitReady)
            .Add(component => component.ActiveCommand, WorkbenchCommandKind.Compile));

        foreach (var command in new[]
                 {
                     "create", "author", "compile", "session", "stimulus", "step",
                 })
        {
            await Assert.That(IsDisabled(rendered, command)).IsTrue();
        }
    }

    [Test]
    public async Task WorkbenchCommandExecution_RunAsyncWhileBusy_InvokesOnlyFirstCommand()
    {
        var execution = new WorkbenchCommandExecution();
        var firstStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var invocationCount = 0;

        var first = execution.RunAsync(
            WorkbenchCommandKind.Author,
            async () =>
            {
                invocationCount++;
                firstStarted.SetResult();
                await releaseFirst.Task;
            });
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await execution.RunAsync(
            WorkbenchCommandKind.Compile,
            () =>
            {
                invocationCount++;
                return Task.CompletedTask;
            });

        using (Assert.Multiple())
        {
            await Assert.That(invocationCount).IsEqualTo(1);
            await Assert.That(execution.ActiveCommand).IsEqualTo(WorkbenchCommandKind.Author);
        }

        releaseFirst.SetResult();
        await first;
        await Assert.That(execution.ActiveCommand).IsNull();
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
    public async Task WorkbenchStatusState_InteractiveWithoutProject_ReportsConnected()
    {
        var state = WorkbenchStatusState.From(projection: null, isInteractive: true);

        using (Assert.Multiple())
        {
            await Assert.That(state.IsConnected).IsTrue();
            await Assert.That(state.Connection).IsEqualTo("Connected");
            await Assert.That(state.Compilation).IsEqualTo("Not requested");
            await Assert.That(state.LogicalTime).IsEqualTo("—");
        }
    }

    private static BunitContext CreateContext()
    {
        var context = new BunitContext();
        context.Services.AddFluentUIComponents();
        return context;
    }

    private static bool IsDisabled(IRenderedComponent<WorkbenchCommandBar> rendered, string command)
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
}
