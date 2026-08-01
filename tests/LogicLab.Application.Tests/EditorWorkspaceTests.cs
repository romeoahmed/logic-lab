using LogicLab.Application.Workspaces;
using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;

namespace LogicLab.Application.Tests;

public sealed class EditorWorkspaceTests
{
    [Test]
    public async Task Execute_ValidNarrowCircuit_ObservesProbeAcrossOneStep()
    {
        var workspace = new EditorWorkspace();
        var opened = await Open(workspace);
        var revision = opened.Projection.ProjectRevision;
        var definitionId = revision.Document.EntryCircuitDefinitionId;

        await Apply(workspace, opened.WorkspaceId, Place(
            definitionId,
            "source.input",
            [
                new ComponentParameterBinding("width", new Unsigned32ParameterValue(1)),
                new ComponentParameterBinding(
                    "initialValue",
                    new LogicVectorParameterValue([LogicValue.Zero])),
            ],
            new GridPoint(0, 0)));
        var input = FindByContract(workspace, opened.WorkspaceId, "source.input");

        await Apply(workspace, opened.WorkspaceId, Place(
            definitionId,
            "logic.not",
            [new ComponentParameterBinding("width", new Unsigned32ParameterValue(1))],
            new GridPoint(4, 0)));
        var logicNot = FindByContract(workspace, opened.WorkspaceId, "logic.not");

        await Apply(workspace, opened.WorkspaceId, Place(
            definitionId,
            "sink.output",
            [
                new ComponentParameterBinding("width", new Unsigned32ParameterValue(1)),
                new ComponentParameterBinding("radix", new ChoiceParameterValue("binary")),
            ],
            new GridPoint(8, 0)));
        var output = FindByContract(workspace, opened.WorkspaceId, "sink.output");

        await Apply(workspace, opened.WorkspaceId, new ConnectTerminalsIntent(
            [
                Terminal(definitionId, input, "Q"),
                Terminal(definitionId, logicNot, "A"),
            ]));
        await Apply(workspace, opened.WorkspaceId, new ConnectTerminalsIntent(
            [
                Terminal(definitionId, logicNot, "Q"),
                Terminal(definitionId, output, "D"),
            ]));

        var compiled = workspace.Execute(
            new RequestCompilation(opened.WorkspaceId),
            CancellationToken.None);
        var sessionCreated = workspace.Execute(
            new CreateSession(opened.WorkspaceId),
            CancellationToken.None);
        var initial = Read(workspace, opened.WorkspaceId);

        await Assert.That(compiled).IsTypeOf<CompilationPublished>();
        await Assert.That(sessionCreated).IsTypeOf<SimulationSessionCreated>();
        await Assert.That(initial.Simulation).IsNotNull();
        using (Assert.Multiple())
        {
            await Assert.That(initial.Simulation!.LogicalTime).IsEqualTo(0UL);
            await Assert.That(initial.Simulation.Probes).Count().IsEqualTo(1);
            await Assert.That(initial.Simulation.Probes[0].Value).IsEquivalentTo(
                new[] { LogicValue.One });
        }

        var scheduled = workspace.Execute(
            new ScheduleInputStimulus(
                opened.WorkspaceId,
                1,
                [new InputStimulusAssignment(input.Id, [LogicValue.One])]),
            CancellationToken.None);
        var stepped = workspace.Execute(
            new StepSession(opened.WorkspaceId),
            CancellationToken.None);
        var afterStep = Read(workspace, opened.WorkspaceId);

        await Assert.That(scheduled).IsTypeOf<StimulusScheduled>();
        await Assert.That(stepped).IsTypeOf<SessionStepped>();
        await Assert.That(afterStep.Simulation).IsNotNull();
        using (Assert.Multiple())
        {
            await Assert.That(afterStep.Simulation!.LogicalTime).IsEqualTo(1UL);
            await Assert.That(afterStep.Simulation.Probes[0].Value).IsEquivalentTo(
                new[] { LogicValue.Zero });
        }
    }

    [Test]
    public async Task Execute_IncompleteCircuit_DoesNotPublishArtifactOrCreateSession()
    {
        var workspace = new EditorWorkspace();
        var opened = await Open(workspace);
        var definitionId = opened.Projection.ProjectRevision.Document.EntryCircuitDefinitionId;
        await Apply(workspace, opened.WorkspaceId, Place(
            definitionId,
            "logic.not",
            [new ComponentParameterBinding("width", new Unsigned32ParameterValue(1))],
            new GridPoint(4, 0)));

        var compilation = workspace.Execute(
            new RequestCompilation(opened.WorkspaceId),
            CancellationToken.None);
        var session = workspace.Execute(
            new CreateSession(opened.WorkspaceId),
            CancellationToken.None);
        var projection = Read(workspace, opened.WorkspaceId);

        await Assert.That(compilation).IsTypeOf<WorkspaceCommandRejected>();
        await Assert.That(session).IsTypeOf<WorkspaceCommandRejected>();
        using (Assert.Multiple())
        {
            await Assert.That(((WorkspaceCommandRejected)compilation).Code)
                .IsEqualTo("compilation_rejected");
            await Assert.That(((WorkspaceCommandRejected)session).Code)
                .IsEqualTo("compilation_required");
            await Assert.That(projection.Compilation.Status)
                .IsEqualTo(CompilationPublicationStatus.Rejected);
            await Assert.That(projection.Compilation.ArtifactKey).IsNull();
            await Assert.That(projection.Simulation).IsNull();
        }
    }

    [Test]
    public async Task Execute_CancelledCompilation_DoesNotChangeProjection()
    {
        var workspace = new EditorWorkspace();
        var opened = await Open(workspace);
        var before = Read(workspace, opened.WorkspaceId);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var outcome = workspace.Execute(
            new RequestCompilation(opened.WorkspaceId),
            cancellation.Token);
        var after = Read(workspace, opened.WorkspaceId);

        await Assert.That(outcome).IsTypeOf<WorkspaceCommandRejected>();
        using (Assert.Multiple())
        {
            await Assert.That(((WorkspaceCommandRejected)outcome).Code)
                .IsEqualTo("operation_cancelled");
            await Assert.That(after.ProjectionVersion).IsEqualTo(before.ProjectionVersion);
            await Assert.That(after.Compilation.Status)
                .IsEqualTo(CompilationPublicationStatus.NotRequested);
            await Assert.That(after.Compilation.ArtifactKey).IsNull();
        }
    }

    private static async Task<WorkspaceOpened> Open(EditorWorkspace workspace)
    {
        var outcome = workspace.Open(
            new CreateSandbox("Test project", "Main"),
            CancellationToken.None);
        await Assert.That(outcome).IsTypeOf<WorkspaceOpened>();
        return (WorkspaceOpened)outcome;
    }

    private static async Task Apply(
        EditorWorkspace workspace,
        WorkspaceId workspaceId,
        EditIntent intent)
    {
        var outcome = workspace.Execute(
            new ApplyEdit(workspaceId, intent),
            CancellationToken.None);
        await Assert.That(outcome).IsTypeOf<AuthoringCommitted>();
    }

    private static WorkspaceProjection Read(EditorWorkspace workspace, WorkspaceId workspaceId)
    {
        return ((ProjectionSnapshot)workspace.Read(workspaceId, CancellationToken.None)).Projection;
    }

    private static ComponentInstance FindByContract(
        EditorWorkspace workspace,
        WorkspaceId workspaceId,
        string contractId)
    {
        return Read(workspace, workspaceId)
            .ProjectRevision
            .Document
            .EntryCircuitDefinition
            .ComponentInstances
            .Single(instance => instance.ContractKey.ContractId == contractId);
    }

    private static PlaceComponentInstanceIntent Place(
        CircuitDefinitionId definitionId,
        string contractId,
        ComponentParameterBinding[] parameters,
        GridPoint origin)
    {
        return new PlaceComponentInstanceIntent(
            definitionId,
            new ComponentContractKey(CoreLibrarySchema.LibraryId, contractId),
            parameters,
            new ComponentPlacement(origin));
    }

    private static InstanceTerminalReference Terminal(
        CircuitDefinitionId definitionId,
        ComponentInstance component,
        string portId)
    {
        return new InstanceTerminalReference(definitionId, component.Id, portId);
    }
}
