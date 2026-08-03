using LogicLab.Application.Workspaces;

namespace LogicLab.Application.Tests;

public sealed class ApplicationPublicSurfaceTests
{
    [Test]
    public async Task ApplicationAssembly_ExportedTypes_MatchWorkspaceContractAllowlist()
    {
        var exportedNames = typeof(IEditorWorkspace).Assembly
            .GetExportedTypes()
            .Select(type => type.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var expectedNames = new[]
        {
            "ApplyEdit",
            "AuthoringCommitted",
            "CloseWorkspace",
            "CompilationProjection",
            "CompilationPublicationStatus",
            "CompilationPublished",
            "CreateSandbox",
            "CreateSession",
            "EditorWorkspaceFactory",
            "IEditorWorkspace",
            "InputStimulusAssignment",
            "OpenWorkspaceRequest",
            "ProbeProjection",
            "ProjectionSnapshot",
            "RequestCompilation",
            "ScheduleInputStimulus",
            "SchedulingPolicy",
            "SessionStepped",
            "SimulationProjection",
            "SimulationSessionCreated",
            "StepSession",
            "StimulusScheduled",
            "WorkspaceAuthoringLimits",
            "WorkspaceClosed",
            "WorkspaceCommand",
            "WorkspaceCommandOutcome",
            "WorkspaceCommandRejected",
            "WorkspaceId",
            "WorkspaceOpenOutcome",
            "WorkspaceOpened",
            "WorkspaceOpenRejected",
            "WorkspacePolicy",
            "WorkspaceProjection",
            "WorkspaceReadOutcome",
            "WorkspaceReadRejected",
        };

        await Assert.That(exportedNames).IsEquivalentTo(expectedNames);
    }

    [Test]
    public async Task ApplicationContracts_ValidatedRecords_DoNotExposeLegacyDeconstruction()
    {
        Type[] validatedRecordTypes =
        [
            typeof(WorkspaceId),
            typeof(CreateSandbox),
            typeof(ApplyEdit),
            typeof(RequestCompilation),
            typeof(CreateSession),
            typeof(StepSession),
            typeof(CloseWorkspace),
        ];

        var legacyDeconstructors = validatedRecordTypes
            .SelectMany(type => type.GetMethods())
            .Where(method => method.Name == "Deconstruct")
            .Select(method => method.DeclaringType!.Name)
            .ToArray();

        await Assert.That(legacyDeconstructors).IsEmpty();
    }

    [Test]
    public async Task WorkspacePolicy_AuthoringDimensions_AreGroupedAsOneValue()
    {
        var policyType = typeof(WorkspacePolicy);
        var authoringLimitsProperty = policyType.GetProperty("AuthoringLimits");
        var constructorParameterCounts = policyType.GetConstructors()
            .Select(constructor => constructor.GetParameters().Length)
            .Order()
            .ToArray();

        using (Assert.Multiple())
        {
            await Assert.That(authoringLimitsProperty).IsNotNull();
            await Assert.That(authoringLimitsProperty!.PropertyType)
                .IsEqualTo(typeof(WorkspaceAuthoringLimits));
            await Assert.That(policyType.GetProperty("AuthoringDefinitionCountLimit"))
                .IsNull();
            await Assert.That(policyType.GetProperty("AuthoringEntityCountLimit"))
                .IsNull();
            await Assert.That(policyType.GetProperty("AuthoringCommandItemCountLimit"))
                .IsNull();
            await Assert.That(constructorParameterCounts).IsEquivalentTo([2, 3]);
        }
    }
}
