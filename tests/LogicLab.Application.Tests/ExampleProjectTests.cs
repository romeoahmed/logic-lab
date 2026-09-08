using LogicLab.Application.Examples;
using LogicLab.Application.Workspaces;

namespace LogicLab.Application.Tests;

internal sealed class ExampleProjectTests
{
    [Test]
    [Arguments(ExampleProject.Inverter)]
    [Arguments(ExampleProject.Steering)]
    [Arguments(ExampleProject.CarryLookahead)]
    [Arguments(ExampleProject.BitSerial)]
    public async Task OpenAsync_Example_PublishesCompleteCompiledSandboxWithFreshHistory(
        ExampleProject example)
    {
        await using var workspace = TestEditorWorkspaceFactory.Create(WorkspaceBuild.TestFingerprint);

        var first = (WorkspaceOpened)await workspace.OpenAsync(
            new OpenExample(example, AnonymousWorkspaceCaller.Instance), CancellationToken.None);
        var second = (WorkspaceOpened)await workspace.OpenAsync(
            new OpenExample(example, AnonymousWorkspaceCaller.Instance), CancellationToken.None);

        var projection = first.Projection;
        var document = projection.ProjectRevision.Document;
        using (Assert.Multiple())
        {
            await Assert.That(projection.Compilation).IsTypeOf<CompilationPublishedProjection>();
            await Assert.That(projection.ProjectionVersion).IsEqualTo(1UL);
            await Assert.That(projection.Durability).IsTypeOf<SandboxWorkspaceDurabilityProjection>();
            await Assert.That(projection.Simulation).IsNull();
            await Assert.That(projection.History).IsEqualTo(new TransactionHistoryAvailability(false, false, 1));
            await Assert.That(document.EntryCircuitDefinition.Nets).IsNotEmpty();
            await Assert.That(document.EntryCircuitDefinition.WireGeometries).IsNotEmpty();
            await Assert.That(first.WorkspaceId).IsNotEqualTo(second.WorkspaceId);
            await Assert.That(projection.ProjectRevision.RevisionId)
                .IsNotEqualTo(second.Projection.ProjectRevision.RevisionId);
            await Assert.That(document.ProjectId).IsEqualTo(second.Projection.ProjectRevision.Document.ProjectId);
        }
    }

    [Test]
    public async Task OpenAsync_ExampleCompilationCancelled_ReleasesAdmissionWithoutPartialWorkspace()
    {
        using var cancellation = new CancellationTokenSource();
        var operations = WorkspaceModuleOperations.Production with
        {
            Compile = (_, token) =>
            {
                cancellation.Cancel();
                token.ThrowIfCancellationRequested();
                throw new InvalidOperationException("The compilation must observe cancellation.");
            },
        };
        var policy = new WorkspacePolicy(
            "examples-test", "1", 1, 1, 1, TimeSpan.FromHours(1),
            WorkspaceAuthoringLimits.Default, 16, 16, TimeSpan.FromMinutes(30), ulong.MaxValue,
            DurableDisplayNameLimits.Default, DurableProjectCatalogLimits.Default);
        await using var workspace = TestEditorWorkspaceFactory.CreateForTesting(operations, workspacePolicy: policy);

        var rejected = (WorkspaceOpenRejected)await workspace.OpenAsync(
            new OpenExample(ExampleProject.CarryLookahead, AnonymousWorkspaceCaller.Instance), cancellation.Token);
        var replacement = await workspace.OpenAsync(
            new CreateSandbox("Replacement", "Main", AnonymousWorkspaceCaller.Instance), CancellationToken.None);

        await Assert.That(rejected.Code).IsEqualTo(WorkspaceOutcomeReasons.WorkspaceCancelled);
        await Assert.That(replacement).IsTypeOf<WorkspaceOpened>();
    }
}
