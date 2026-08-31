using System.Reflection;
using LogicLab.Application.Workspaces;
using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.Engine;
using LogicLab.Engine.Compilation;
using LogicLab.Engine.Simulation;
using TUnit.Assertions.Enums;

namespace LogicLab.Application.Tests;

internal sealed class EditorWorkspaceTraceTests
{
    [Test]
    public async Task ReadAsync_Transitions_TransfersNormalizedPackedValuesWithoutRuntimeStorage(
        CancellationToken cancellationToken)
    {
        await using var workspace = TestEditorWorkspaceFactory.Create(
            WorkspaceBuild.TestFingerprint);
        var session = await OpenVectorSessionAsync(workspace, cancellationToken);
        var simulation = session.Projection.Simulation!;

        var outcome = await workspace.ReadAsync(
            Query(session),
            new LogicLab.Application.Workspaces.ReadTraceWindow(
                new TraceWindowRequest(
                    simulation.SessionId,
                    simulation.CompilationArtifactKey,
                    [.. simulation.Probes.Select(probe => probe.ProbeId)],
                    new TraceTimeRange(0, 1),
                    TraceTransitionsRequest.Instance,
                    afterSequence: null)),
            cancellationToken);

        var read = (await Assert.That(outcome).IsTypeOf<TraceWindowRead>())!;
        var transitions = (await Assert.That(read.Outcome)
            .IsTypeOf<TraceTransitionsWindow>())!;
        var transition = await Assert.That(transitions.Transitions).HasSingleItem();
        using (Assert.Multiple())
        {
            await Assert.That(transition.ProbeId).IsEqualTo(simulation.Probes[0].ProbeId);
            await Assert.That(transition.LogicalTime).IsEqualTo("0");
            await Assert.That(transition.Sequence).IsEqualTo("1");
            await Assert.That(transition.Value.Width).IsEqualTo(4U);
            await Assert.That(transition.Value.Encoding).IsEqualTo("logic4-2bit-v1");
            await Assert.That(transition.Value.Data).IsEquivalentTo([(byte)0b1010_0100]);
            await Assert.That(transitions.CoveredRange).IsEqualTo(new TraceTimeRange(0, 1));
        }

        var afterRead = await ReadProjectionAsync(workspace, session, cancellationToken);
        using (Assert.Multiple())
        {
            await Assert.That(afterRead.ProjectionVersion)
                .IsEqualTo(session.Projection.ProjectionVersion);
            await Assert.That(afterRead.Simulation!.SessionVersion)
                .IsEqualTo(simulation.SessionVersion);
        }
    }

    [Test]
    public async Task ReadAsync_VisualSummary_TransfersExactAggregationAndBuckets(
        CancellationToken cancellationToken)
    {
        await using var workspace = TestEditorWorkspaceFactory.Create(
            WorkspaceBuild.TestFingerprint);
        var session = await OpenVectorSessionAsync(workspace, cancellationToken);
        var simulation = session.Projection.Simulation!;

        var outcome = await workspace.ReadAsync(
            Query(session),
            new LogicLab.Application.Workspaces.ReadTraceWindow(
                new TraceWindowRequest(
                    simulation.SessionId,
                    simulation.CompilationArtifactKey,
                    [.. simulation.Probes.Select(probe => probe.ProbeId)],
                    new TraceTimeRange(0, 1),
                    new TraceVisualSummaryRequest(
                        maxPoints: 1,
                        TraceVisualSummaryRequest.LogicEnvelopeV1),
                    afterSequence: null)),
            cancellationToken);

        var read = (await Assert.That(outcome).IsTypeOf<TraceWindowRead>())!;
        var summary = (await Assert.That(read.Outcome).IsTypeOf<TraceSummaryWindow>())!;
        var bucket = await Assert.That(summary.Buckets).HasSingleItem();
        using (Assert.Multiple())
        {
            await Assert.That(summary.Aggregation)
                .IsEqualTo(TraceVisualSummaryRequest.LogicEnvelopeV1);
            await Assert.That(bucket.Range).IsEqualTo(new TraceTimeRange(0, 1));
            await Assert.That(bucket.FirstValue.Data)
                .IsEquivalentTo([(byte)0b1010_0100]);
            await Assert.That(bucket.LastValue.Data)
                .IsEquivalentTo([(byte)0b1010_0100]);
            await Assert.That(bucket.HadTransition).IsFalse();
            await Assert.That(bucket.HadMixedValues).IsFalse();
        }
    }

    [Test]
    public async Task ReadAsync_StaleArtifact_ReturnsArtifactChangedGapEvidence(
        CancellationToken cancellationToken)
    {
        await using var workspace = TestEditorWorkspaceFactory.Create(
            WorkspaceBuild.TestFingerprint);
        var session = await OpenVectorSessionAsync(workspace, cancellationToken);
        var simulation = session.Projection.Simulation!;
        var staleArtifact = simulation.CompilationArtifactKey with
        {
            CompilerSemanticVersion = "stale-artifact",
        };

        var outcome = await workspace.ReadAsync(
            Query(session),
            new LogicLab.Application.Workspaces.ReadTraceWindow(
                new TraceWindowRequest(
                    simulation.SessionId,
                    staleArtifact,
                    [.. simulation.Probes.Select(probe => probe.ProbeId)],
                    new TraceTimeRange(0, 1),
                    TraceTransitionsRequest.Instance,
                    afterSequence: null)),
            cancellationToken);

        var read = (await Assert.That(outcome).IsTypeOf<TraceWindowRead>())!;
        var unavailable = (await Assert.That(read.Outcome)
            .IsTypeOf<TraceWindowUnavailable>())!;
        using (Assert.Multiple())
        {
            await Assert.That(unavailable.Reason)
                .IsEqualTo(TraceWindowUnavailableReason.ArtifactChanged);
            await Assert.That(unavailable.EarliestAvailable)
                .IsEqualTo(simulation.TraceCursor.EarliestAvailableSequence);
            await Assert.That(unavailable.LatestSequence)
                .IsEqualTo(simulation.TraceCursor.LatestSequence);
        }
    }

    [Test]
    public async Task ReadAsync_UnknownProbe_ReturnsSessionPreconditionFailure(
        CancellationToken cancellationToken)
    {
        await using var workspace = TestEditorWorkspaceFactory.Create(
            WorkspaceBuild.TestFingerprint);
        var session = await OpenVectorSessionAsync(workspace, cancellationToken);
        var simulation = session.Projection.Simulation!;
        var unknownProbe = Identifier<ProbeId>("unknown-probe");

        var outcome = await workspace.ReadAsync(
            Query(session),
            new LogicLab.Application.Workspaces.ReadTraceWindow(
                new TraceWindowRequest(
                    simulation.SessionId,
                    simulation.CompilationArtifactKey,
                    [unknownProbe],
                    new TraceTimeRange(0, 1),
                    TraceTransitionsRequest.Instance,
                    afterSequence: null)),
            cancellationToken);

        var rejected = (await Assert.That(outcome)
            .IsTypeOf<WorkspaceReadRejected>())!;
        await Assert.That(rejected.Code)
            .IsEqualTo(WorkspaceOutcomeReasons.SessionPreconditionFailed);
    }

    [Test]
    public async Task TransferRecords_NoncanonicalValues_RejectBeforeCrossingTheSeam()
    {
        var probeId = Identifier<ProbeId>("probe-a");

        using (Assert.Multiple())
        {
            await Assert.That(LogicVectorTransferV1.From(new LogicVector(
                    [LogicValue.Zero, LogicValue.One, LogicValue.X, LogicValue.Z])).Data)
                .IsEquivalentTo([(byte)0b1110_0100]);
            await Assert.That(() => new LogicVectorTransferV1(
                    width: 1,
                    LogicVectorTransferV1.Logic4TwoBitV1,
                    [(byte)0b0000_0100]))
                .ThrowsExactly<ArgumentException>();
            await Assert.That(() => new TraceTransitionTransfer(
                    probeId,
                    logicalTime: "01",
                    sequence: "1",
                    new LogicVectorTransferV1(
                        1,
                        LogicVectorTransferV1.Logic4TwoBitV1,
                        [0])))
                .ThrowsExactly<ArgumentException>();
        }
    }

    private static async Task<TraceWorkspace> OpenVectorSessionAsync(
        IEditorWorkspace workspace,
        CancellationToken cancellationToken)
    {
        var opened = (WorkspaceOpened)await workspace.OpenAsync(
            new CreateSandbox(
                "Trace project",
                "Main",
                AnonymousWorkspaceCaller.Instance),
            cancellationToken);
        var attached = await EditorWorkspaceTestDriver.AttachAsync(
            workspace,
            opened.WorkspaceId,
            cancellationToken);
        var definitionId = attached.Projection.ProjectRevision.Document
            .EntryCircuitDefinitionId;
        await ApplyAsync(
            workspace,
            opened.WorkspaceId,
            attached,
            new PlaceComponentInstanceIntent(
                definitionId,
                Contract("source.input"),
                [
                    new ComponentParameterBinding(
                        "width",
                        new Unsigned32ParameterValue(4)),
                    new ComponentParameterBinding(
                        "initialValue",
                        new LogicVectorParameterValue(
                            [LogicValue.Zero, LogicValue.One, LogicValue.X, LogicValue.X])),
                ],
                new ComponentPlacement(new GridPoint(0, 0)),
                "Vector input"),
            cancellationToken);
        var afterInput = await ReadProjectionAsync(
            workspace,
            new TraceWorkspace(opened.WorkspaceId, attached, attached.Projection),
            cancellationToken);
        var input = FindLibrary(afterInput, "source.input");
        await ApplyAsync(
            workspace,
            opened.WorkspaceId,
            attached,
            new PlaceComponentInstanceIntent(
                definitionId,
                Contract("sink.output"),
                [
                    new ComponentParameterBinding(
                        "width",
                        new Unsigned32ParameterValue(4)),
                    new ComponentParameterBinding(
                        "radix",
                        new ChoiceParameterValue("binary")),
                ],
                new ComponentPlacement(new GridPoint(8, 0)),
                "Vector output"),
            cancellationToken);
        var afterOutput = await ReadProjectionAsync(
            workspace,
            new TraceWorkspace(opened.WorkspaceId, attached, afterInput),
            cancellationToken);
        var output = FindLibrary(afterOutput, "sink.output");
        await ApplyAsync(
            workspace,
            opened.WorkspaceId,
            attached,
            new ConnectTerminalsIntent(
                [
                    new InstanceTerminalReference(definitionId, input.Id, "Q"),
                    new InstanceTerminalReference(definitionId, output.Id, "D"),
                ]),
            cancellationToken);
        var beforeCompilation = await ReadProjectionAsync(
            workspace,
            new TraceWorkspace(opened.WorkspaceId, attached, afterOutput),
            cancellationToken);
        _ = await workspace.DispatchAsync(
            new RequestCompilation(
                Command(opened.WorkspaceId, attached),
                EditorWorkspaceTestDriver.Compilation(beforeCompilation)),
            cancellationToken);
        var compiled = await EditorWorkspaceTestDriver.WaitForCompilationAsync(
            workspace,
            opened.WorkspaceId,
            attached,
            cancellationToken);
        _ = await workspace.DispatchAsync(
            new CreateSession(
                Command(opened.WorkspaceId, attached),
                EditorWorkspaceTestDriver.SessionCreation(compiled)),
            cancellationToken);
        var projection = await ReadProjectionAsync(
            workspace,
            new TraceWorkspace(opened.WorkspaceId, attached, compiled),
            cancellationToken);
        return new TraceWorkspace(opened.WorkspaceId, attached, projection);
    }

    private static async Task ApplyAsync(
        IEditorWorkspace workspace,
        WorkspaceId workspaceId,
        Attached attached,
        EditIntent intent,
        CancellationToken cancellationToken)
    {
        var current = await ReadProjectionAsync(
            workspace,
            new TraceWorkspace(workspaceId, attached, attached.Projection),
            cancellationToken);
        var outcome = await workspace.DispatchAsync(
            new ApplyEdit(
                Command(workspaceId, attached),
                new AuthoringPrecondition(current.ProjectRevision.RevisionId),
                intent),
            cancellationToken);
        if (outcome is WorkspaceCommandRejected rejected)
        {
            throw new InvalidOperationException(
                $"{rejected.Code}:{string.Join(',', rejected.DiagnosticCodes)}");
        }

        await Assert.That(outcome).IsTypeOf<AuthoringCommitted>();
    }

    private static async Task<WorkspaceProjection> ReadProjectionAsync(
        IEditorWorkspace workspace,
        TraceWorkspace session,
        CancellationToken cancellationToken)
    {
        var outcome = await workspace.ReadAsync(
            Query(session),
            ReadProjection.Instance,
            cancellationToken);
        return ((ProjectionSnapshot)outcome).Projection;
    }

    private static WorkspaceQueryContext Query(TraceWorkspace session) => new(
        session.WorkspaceId,
        session.Attached.AttachmentId,
        session.Attached.Generation,
        AnonymousWorkspaceCaller.Instance);

    private static WorkspaceCommandContext Command(
        WorkspaceId workspaceId,
        Attached attached) => new(
            workspaceId,
            attached.AttachmentId,
            attached.Generation,
            new ClientIntentId(Guid.CreateVersion7().ToString("N")),
            AnonymousWorkspaceCaller.Instance);

    private static ComponentContractKey Contract(string contractId) => new(
        CoreLibrarySchema.LibraryId,
        contractId);

    private static ComponentInstance FindLibrary(
        WorkspaceProjection projection,
        string contractId) => projection.ProjectRevision.Document
        .EntryCircuitDefinition.ComponentInstances.Single(instance =>
            instance.Target is LibraryComponentTarget library
            && library.ContractKey.ContractId == contractId);

    private static T Identifier<T>(string value) where T : class =>
        (T)(Activator.CreateInstance(
            typeof(T),
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            args: [value],
            culture: null)
            ?? throw new InvalidOperationException(
                $"The test identifier '{typeof(T).Name}' could not be created."));

    private sealed record TraceWorkspace(
        WorkspaceId WorkspaceId,
        Attached Attached,
        WorkspaceProjection Projection);
}
