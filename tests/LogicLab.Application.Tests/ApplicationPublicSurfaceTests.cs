using LogicLab.Application.Workspaces;

namespace LogicLab.Application.Tests;

public sealed class ApplicationPublicSurfaceTests
{
    [Test]
    public async Task ApplicationAssembly_ExportedTypes_MatchWorkspaceContractAllowlist()
    {
        var exportedTypes = typeof(IEditorWorkspace).Assembly
            .GetExportedTypes()
            .ToArray();
        Type[] expectedTypes =
        [
            typeof(ApplyEdit),
            typeof(AuthoringCommitted),
            typeof(CloseWorkspace),
            typeof(CompilationProjection),
            typeof(CompilationPublicationStatus),
            typeof(CompilationPublished),
            typeof(CreateSandbox),
            typeof(CreateSession),
            typeof(EditorWorkspaceFactory),
            typeof(IEditorWorkspace),
            typeof(InputStimulusAssignment),
            typeof(OpenWorkspaceRequest),
            typeof(ProbeProjection),
            typeof(ProjectionSnapshot),
            typeof(RequestCompilation),
            typeof(ScheduleInputStimulus),
            typeof(SchedulingPolicy),
            typeof(SessionStepped),
            typeof(SimulationProjection),
            typeof(SimulationSessionCreated),
            typeof(StepSession),
            typeof(StimulusScheduled),
            typeof(WorkspaceAuthoringLimits),
            typeof(WorkspaceClosed),
            typeof(WorkspaceCommand),
            typeof(WorkspaceCommandOutcome),
            typeof(WorkspaceCommandRejected),
            typeof(WorkspaceId),
            typeof(WorkspaceOpenOutcome),
            typeof(WorkspaceOpened),
            typeof(WorkspaceOpenRejected),
            typeof(WorkspacePolicy),
            typeof(WorkspaceProjection),
            typeof(WorkspaceReadOutcome),
            typeof(WorkspaceReadRejected),
        ];

        await Assert.That(exportedTypes).IsEquivalentTo(expectedTypes);
    }
}
