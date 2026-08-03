using LogicLab.Application.Workspaces;

namespace LogicLab.Application.Tests;

public sealed class ApplicationPublicSurfaceTests
{
    [Test]
    public async Task ApplicationAssembly_ExportedTypes_HideSchedulingImplementation()
    {
        var exportedNames = typeof(IEditorWorkspace).Assembly
            .GetExportedTypes()
            .Select(type => type.FullName ?? type.Name)
            .ToArray();

        using (Assert.Multiple())
        {
            await Assert.That(exportedNames.Any(name => name.Contains(
                "WorkCoordinator",
                StringComparison.Ordinal))).IsFalse();
            await Assert.That(exportedNames.Any(name => name.Contains(
                "CompilationWorkContext",
                StringComparison.Ordinal))).IsFalse();
            await Assert.That(exportedNames.Any(name => name.Contains(
                "WorkItem",
                StringComparison.Ordinal))).IsFalse();
        }
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
