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
}
