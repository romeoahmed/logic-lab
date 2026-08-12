using LogicLab.Application.Workspaces;
using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.ProjectFormat;

namespace LogicLab.Application.Tests;

internal sealed class ImportProjectWorkspaceTests
{
    [Test]
    public async Task OpenAsync_ImportProject_GenesisCompilesBeforePublishingIndependentWorkspace()
    {
        var exported = BeginProject("Imported project");
        var candidate = await RoundTripCandidateAsync(exported);
        await using var workspace = TestEditorWorkspaceFactory.Create(
            WorkspaceBuild.DevelopmentFingerprint);

        var outcome = await workspace.OpenAsync(
            new ImportProject(candidate),
            CancellationToken.None);

        var opened = (await Assert.That(outcome).IsTypeOf<WorkspaceOpened>())!;
        using (Assert.Multiple())
        {
            await Assert.That(opened.Projection.ProjectRevision.Document.ProjectId)
                .IsEqualTo(exported.Document.ProjectId);
            await Assert.That(opened.Projection.ProjectRevision.RevisionId)
                .IsNotEqualTo(exported.RevisionId);
            await Assert.That(opened.Projection.Compilation)
                .IsTypeOf<CompilationPublishedProjection>();
            await Assert.That(opened.Projection.ProjectionVersion).IsEqualTo(1UL);
            await Assert.That(opened.Projection.Durability)
                .IsTypeOf<SandboxWorkspaceDurabilityProjection>();
        }
    }

    [Test]
    public async Task OpenAsync_ImportCompilationRejected_PublishesNothingAndLeavesOriginUnchanged()
    {
        var candidate = await RoundTripCandidateAsync(CreateIncompleteRevision());
        await using var workspace = TestEditorWorkspaceFactory.Create(
            WorkspaceBuild.DevelopmentFingerprint,
            workspacePolicy: WorkspacePolicyWithLimit(2));
        var origin = (WorkspaceOpened)await workspace.OpenAsync(
            new CreateSandbox("Origin", "Main"),
            CancellationToken.None);
        var attached = await EditorWorkspaceTestDriver.AttachAsync(
            workspace,
            origin.WorkspaceId);
        var before = await ReadAsync(workspace, origin, attached);

        var outcome = await workspace.OpenAsync(
            new ImportProject(candidate),
            CancellationToken.None);
        var after = await ReadAsync(workspace, origin, attached);

        var rejected = (await Assert.That(outcome)
            .IsTypeOf<WorkspaceOpenRejected>())!;
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Code).IsEqualTo("compilation_invalid");
            await Assert.That(rejected.DiagnosticCodes).IsNotEmpty();
            await Assert.That(after.ProjectRevision).IsEqualTo(before.ProjectRevision);
            await Assert.That(after.ProjectionVersion).IsEqualTo(before.ProjectionVersion);
            await Assert.That(after.Compilation).IsEqualTo(before.Compilation);
        }
    }

    [Test]
    public async Task OpenAsync_RejectedImport_ReleasesReservedWorkspaceCapacity()
    {
        var candidate = await RoundTripCandidateAsync(CreateIncompleteRevision());
        await using var workspace = TestEditorWorkspaceFactory.Create(
            WorkspaceBuild.DevelopmentFingerprint,
            workspacePolicy: WorkspacePolicyWithLimit(1));

        var rejected = await workspace.OpenAsync(
            new ImportProject(candidate),
            CancellationToken.None);
        var replacement = await workspace.OpenAsync(
            new CreateSandbox("Replacement", "Main"),
            CancellationToken.None);

        using (Assert.Multiple())
        {
            await Assert.That(rejected).IsTypeOf<WorkspaceOpenRejected>();
            await Assert.That(replacement).IsTypeOf<WorkspaceOpened>();
        }
    }

    [Test]
    public async Task ImportAsync_CarrierLimitRejectsBeforeWorkspaceAllocation()
    {
        await using var workspace = TestEditorWorkspaceFactory.Create(
            WorkspaceBuild.DevelopmentFingerprint,
            workspacePolicy: WorkspacePolicyWithLimit(1));
        var limits = PackagePolicy.Development.Limits.ToArray();
        limits[(int)PackageDimension.CarrierBytes] = new PackageLimit(
            PackageDimension.CarrierBytes,
            4);
        var workflow = new ProjectImportWorkflow(
            workspace,
            new PackagePolicy("import-test-package", "1", limits));
        await using var source = new MemoryStream([1, 2, 3, 4, 5]);

        var outcome = await workflow.ImportAsync(source, CancellationToken.None);
        var replacement = await workspace.OpenAsync(
            new CreateSandbox("Replacement", "Main"),
            CancellationToken.None);

        var rejected = (await Assert.That(outcome)
            .IsTypeOf<WorkspaceOpenRejected>())!;
        using (Assert.Multiple())
        {
            await Assert.That(workflow.MaximumCarrierBytes).IsEqualTo(4L);
            await Assert.That(rejected.Code).IsEqualTo("package_limit_exceeded");
            await Assert.That(replacement).IsTypeOf<WorkspaceOpened>();
        }
    }

    [Test]
    public async Task OpenAsync_ImportCompilationCancellation_PublishesNothingAndReleasesAdmission()
    {
        using var cancellation = new CancellationTokenSource();
        var candidate = await RoundTripCandidateAsync(BeginProject("Cancelled import"));
        var operations = WorkspaceModuleOperations.Production with
        {
            Compile = (_, operationCancellationToken) =>
            {
                cancellation.Cancel();
                operationCancellationToken.ThrowIfCancellationRequested();
                throw new InvalidOperationException(
                    "The Compilation lane did not observe request cancellation.");
            },
        };
        await using var workspace = TestEditorWorkspaceFactory.CreateForTesting(
            operations,
            workspacePolicy: WorkspacePolicyWithLimit(1));

        var outcome = await workspace.OpenAsync(
            new ImportProject(candidate),
            cancellation.Token);
        var replacement = await workspace.OpenAsync(
            new CreateSandbox("Replacement", "Main"),
            CancellationToken.None);

        var rejected = (await Assert.That(outcome)
            .IsTypeOf<WorkspaceOpenRejected>())!;
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Code).IsEqualTo("workspace_cancelled");
            await Assert.That(replacement).IsTypeOf<WorkspaceOpened>();
        }
    }

    [Test]
    [Arguments(true, "workspace_infrastructure_failure")]
    [Arguments(false, "workspace_internal_defect")]
    public async Task OpenAsync_ImportCompilationFailure_PublishesNothingAndReleasesAdmission(
        bool infrastructureFailure,
        string expectedCode)
    {
        var candidate = await RoundTripCandidateAsync(BeginProject("Failed import"));
        var operations = WorkspaceModuleOperations.Production with
        {
            Compile = (_, _) => throw (infrastructureFailure
                ? (Exception)new IOException("Compilation dependency unavailable.")
                : new InvalidOperationException("Compilation implementation defect.")),
        };
        await using var workspace = TestEditorWorkspaceFactory.CreateForTesting(
            operations,
            workspacePolicy: WorkspacePolicyWithLimit(1));

        var outcome = await workspace.OpenAsync(
            new ImportProject(candidate),
            CancellationToken.None);
        var replacement = await workspace.OpenAsync(
            new CreateSandbox("Replacement", "Main"),
            CancellationToken.None);

        var rejected = (await Assert.That(outcome)
            .IsTypeOf<WorkspaceOpenRejected>())!;
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Code).IsEqualTo(expectedCode);
            await Assert.That(replacement).IsTypeOf<WorkspaceOpened>();
        }
    }

    private static async Task<WorkspaceProjection> ReadAsync(
        IEditorWorkspace workspace,
        WorkspaceOpened opened,
        Attached attached)
    {
        var outcome = await workspace.ReadAsync(
            EditorWorkspaceTestDriver.Query(opened.WorkspaceId, attached),
            ReadProjection.Instance,
            CancellationToken.None);
        return ((ProjectionSnapshot)outcome).Projection;
    }

    private static ProjectRevision BeginProject(string displayName)
    {
        return ((ProjectGenesisCommitted)ProjectEditor.Begin(new NewProjectSeed(
            displayName,
            LibrarySnapshot.Core,
            new SymbolProfileReference(
                "TeachingMixed",
                "1.0.0",
                IndicationConvention.Negation),
            "Main"))).Revision;
    }

    private static ProjectRevision CreateIncompleteRevision()
    {
        var revision = BeginProject("Incomplete import");
        return ((EditCommitted)ProjectEditor.Apply(
            revision,
            new PlaceComponentInstanceIntent(
                revision.Document.EntryCircuitDefinitionId,
                new ComponentContractKey(CoreLibrarySchema.LibraryId, "logic.not"),
                [new ComponentParameterBinding(
                    "width",
                    new Unsigned32ParameterValue(1))],
                new ComponentPlacement(new GridPoint(0, 0))))).Revision;
    }

    private static WorkspacePolicy WorkspacePolicyWithLimit(int workspaceLimit)
    {
        return new WorkspacePolicy(
            "import-tests",
            "1",
            workspaceLimit,
            sandboxRetention: TimeSpan.FromMinutes(1),
            authoringLimits: WorkspaceAuthoringLimits.Default,
            historyRevisionCount: 4,
            idempotencyRecordCount: 4,
            detachedRetention: TimeSpan.FromMinutes(1),
            hotSwapPeakBytes: 1_024,
            durableDisplayNameLimits: DurableDisplayNameLimits.Default,
            durableProjectCatalogLimits: DurableProjectCatalogLimits.Default);
    }

    private static async Task<ProjectImportCandidate> RoundTripCandidateAsync(
        ProjectRevision revision)
    {
        await using var carrier = new MemoryStream();
        var write = await ProjectPackage.WriteAsync(
            new ProjectPackageWriteRequest(
                revision,
                carrier,
                PackagePolicy.Development),
            CancellationToken.None);
        if (write is not PackageWriteSucceeded)
        {
            throw new InvalidOperationException("Test package write failed.");
        }

        carrier.Position = 0;
        var read = await ProjectPackage.ReadAsync(
            new ProjectPackageReadRequest(carrier, PackagePolicy.Development),
            CancellationToken.None);
        return read is PackageReadSucceeded succeeded
            ? succeeded.ImportCandidate
            : throw new InvalidOperationException("Test package read failed.");
    }
}
