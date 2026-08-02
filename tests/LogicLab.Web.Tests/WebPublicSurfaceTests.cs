using LogicLab.Web.Components.Pages;

namespace LogicLab.Web.Tests;

public sealed class WebPublicSurfaceTests
{
    [Test]
    public async Task WebAssembly_ExportedTypes_ExcludeRemovedWorkbenchHelpers()
    {
        var exportedNames = typeof(Editor).Assembly
            .GetExportedTypes()
            .Select(type => type.FullName ?? type.Name)
            .ToArray();

        using (Assert.Multiple())
        {
            await Assert.That(exportedNames.Any(name => name.EndsWith(
                ".WorkbenchCommandExecution",
                StringComparison.Ordinal))).IsFalse();
            await Assert.That(exportedNames.Any(name => name.EndsWith(
                ".WorkbenchViewState",
                StringComparison.Ordinal))).IsFalse();
            await Assert.That(exportedNames.Any(name => name.EndsWith(
                ".WorkbenchStatusState",
                StringComparison.Ordinal))).IsFalse();
        }
    }
}
