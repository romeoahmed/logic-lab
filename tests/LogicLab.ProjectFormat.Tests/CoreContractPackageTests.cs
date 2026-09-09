using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using static LogicLab.ComponentTesting.CoreContractFixture;

namespace LogicLab.ProjectFormat.Tests;

internal sealed class CoreContractPackageTests
{
    public static IEnumerable<string> Contracts() => LibrarySnapshot.Core.Contracts.Select(contract => contract.Key.ContractId);

    [Test]
    [MethodDataSource(nameof(Contracts))]
    public async Task Package_CoreContract_RoundTripsExactKeyParametersAndCanonicalBytes(string contractId)
    {
        var revision = CreateRevision(contractId);
        await using var original = new MemoryStream();
        await Assert.That(await ProjectPackage.WriteAsync(new(revision, original, PackagePolicy.Default), CancellationToken.None))
            .IsTypeOf<PackageWriteSucceeded>();
        original.Position = 0;
        var read = await ProjectPackage.ReadAsync(new(original, PackagePolicy.Default), CancellationToken.None);
        var imported = (await Assert.That(read).IsTypeOf<PackageReadSucceeded>())!;
        var restored = ((ProjectGenesisCommitted)ProjectEditor.Begin(new ImportedProjectSeed(imported.ImportCandidate))).Revision;
        var component = restored.Document.EntryCircuitDefinition.ComponentInstances.Single();
        await Assert.That(((LibraryComponentTarget)component.Target).ContractKey)
            .IsEqualTo(new ComponentContractKey(LibrarySnapshot.Core.LibraryId, contractId));
        await Assert.That(component.Id).IsEqualTo(revision.Document.EntryCircuitDefinition.ComponentInstances.Single().Id);
        await Assert.That(restored.Document.LibrarySnapshot.ContentDigest).IsEqualTo(revision.Document.LibrarySnapshot.ContentDigest);
        await using var rewritten = new MemoryStream();
        await Assert.That(await ProjectPackage.WriteAsync(new(restored, rewritten, PackagePolicy.Default), CancellationToken.None))
            .IsTypeOf<PackageWriteSucceeded>();
        await Assert.That(rewritten.ToArray()).IsEquivalentTo(original.ToArray(), TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

}
