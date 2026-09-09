using System.Text;
using System.Text.Json.Nodes;
using LogicLab.ComponentTesting;
using LogicLab.Domain.Authoring;
using static LogicLab.ProjectFormat.Tests.ProjectPackageTestFixture;

namespace LogicLab.ProjectFormat.Tests;

internal sealed partial class ProjectPackageReaderTests
{
    public static IEnumerable<string> CoreContracts() => LibrarySnapshot.Core.Contracts.Select(contract => contract.Key.ContractId);

    [Test]
    [MethodDataSource(nameof(CoreContracts))]
    public async Task ReadAsync_CoreContractWithChangedLibraryIdentity_RejectsBeforeImportPublication(string contractId)
    {
        var revision = CoreContractFixture.CreateRevision(contractId);
        await using var original = await WriteAsync(revision);
        foreach (var mutation in new[] { "id", "version", "digest", "contractLibrary" })
        {
            var entries = ReadEntries(original.Stream);
            var project = JsonNode.Parse(entries["project.json"])!.AsObject();
            if (mutation == "contractLibrary")
            {
                project["circuitDefinitions"]![0]!["componentInstances"]![0]!["target"]!["libraryId"] = "logiclab.other";
            }
            else
            {
                project["libraryReferences"]![0]![mutation] = mutation switch
                {
                    "id" => "logiclab.other",
                    "version" => "2.0.0",
                    "digest" => new string('0', 64),
                    _ => throw new InvalidOperationException(),
                };
            }
            entries["project.json"] = Encoding.UTF8.GetBytes(project.ToJsonString());
            RefreshIntegrity(entries);
            await using var changed = WriteEntries(entries);
            await AssertDiagnostic(await ReadAsync(changed), "package_domain_invalid");
        }
    }
}
