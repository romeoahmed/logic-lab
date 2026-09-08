using LogicLab.Application.Examples;
using LogicLab.Domain.Authoring;
using LogicLab.ProjectFormat;

namespace LogicLab.Web.BrowserTests;

internal static class StarterCircuitFixture
{
    public static async Task<ProjectRevision> LoadAsync(ExampleProject example)
    {
        await using var source = typeof(ExampleProject).Assembly.GetManifestResourceStream(
            $"LogicLab.Application.Examples.Assets.{example}.logiclab")
            ?? throw new InvalidOperationException("The example resource is missing.");
        var read = (PackageReadSucceeded)await ProjectPackage.ReadAsync(
            new ProjectPackageReadRequest(source, PackagePolicy.Default),
            CancellationToken.None);
        return ((ProjectGenesisCommitted)ProjectEditor.Begin(
            new ImportedProjectSeed(read.ImportCandidate))).Revision;
    }
}
