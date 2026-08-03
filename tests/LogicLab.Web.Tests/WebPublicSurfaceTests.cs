using LogicLab.Web.Components.Pages;

namespace LogicLab.Web.Tests;

public sealed class WebPublicSurfaceTests
{
    [Test]
    public async Task WebAssembly_ExportedTypes_MatchComponentContractAllowlist()
    {
        var exportedNames = typeof(Editor).Assembly
            .GetExportedTypes()
            .Select(type => type.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var expectedNames = new[]
        {
            "AccessibleCircuitScene",
            "App",
            "DefinitionNavigator",
            "Editor",
            "Error",
            "HierarchyBreadcrumbItem",
            "Home",
            "MainLayout",
            "NotFound",
            "ProbePanel",
            "Program",
            "ReconnectModal",
            "Routes",
            "TopologyCommandBar",
            "WorkbenchCommandBar",
            "WorkbenchStatusStrip",
            "_Imports",
        };

        await Assert.That(exportedNames).IsEquivalentTo(expectedNames);
    }
}
