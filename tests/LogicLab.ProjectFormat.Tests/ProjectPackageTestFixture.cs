using System.IO.Compression;
using LogicLab.Domain;
using LogicLab.Domain.Authoring;

namespace LogicLab.ProjectFormat.Tests;

internal static class ProjectPackageTestFixture
{
    public static ProjectRevision BeginProject(string displayName, string entryName)
    {
        return ((ProjectGenesisCommitted)ProjectEditor.Begin(
            new NewProjectSeed(
                displayName,
                LibrarySnapshot.Core,
                new SymbolProfileReference(
                    "TeachingMixed",
                    "1.0.0",
                    IndicationConvention.Negation),
                entryName))).Revision;
    }

    public static Dictionary<string, byte[]> ReadEntries(MemoryStream package)
    {
        package.Position = 0;
        using var archive = new ZipArchive(
            package,
            ZipArchiveMode.Read,
            leaveOpen: true);
        return archive.Entries.ToDictionary(
            entry => entry.FullName,
            entry =>
            {
                using var source = entry.Open();
                using var bytes = new MemoryStream();
                source.CopyTo(bytes);
                return bytes.ToArray();
            },
            StringComparer.Ordinal);
    }
}
