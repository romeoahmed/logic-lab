using LogicLab.ComponentTesting;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using TUnit.Assertions.Enums;

namespace LogicLab.Domain.Tests;

internal sealed partial class CoreContractParameterTests
{
    public static IEnumerable<string> Contracts() => LibrarySnapshot.Core.Contracts.Select(contract => contract.Key.ContractId);

    [Test]
    [MethodDataSource(nameof(Contracts))]
    public async Task Apply_CoreContract_InvalidParameterEnvelopesPublishNoRevision(string contractId)
    {
        var revision = CoreContractFixture.CreateRevision(contractId);
        var definition = revision.Document.EntryCircuitDefinition;
        var component = definition.ComponentInstances.Single();
        var valid = component.Parameters.ToArray();
        List<ComponentParameterBinding[]> invalid =
        [
            valid[1..],
            [.. valid, valid[0]],
            [.. valid, new("unregistered", new Unsigned32ParameterValue(1))],
        ];
        foreach (var parameter in LibrarySnapshot.Core.Contracts.Single(contract => contract.Key.ContractId == contractId).Parameters)
        {
            ComponentParameterValue wrongKind = parameter is ChoiceParameterSchema
                ? new Unsigned32ParameterValue(1) : new ChoiceParameterValue("wrong-kind");
            ComponentParameterValue invalidValue = parameter switch
            {
                WidthParameterSchema width => new Unsigned32ParameterValue(width.MinimumValue - 1),
                LogicVectorParameterSchema => new LogicVectorParameterValue([]),
                ChoiceParameterSchema => new ChoiceParameterValue("unregistered"),
                SlicesParameterSchema => new SlicesParameterValue([new(uint.MaxValue, uint.MaxValue), new(0, 1)]),
                WidthsParameterSchema => new WidthsParameterValue([uint.MaxValue, uint.MaxValue]),
                MemoryImageParameterSchema => new MemoryImageParameterValue(MemoryImageId.Create()),
                BinaryLogicParameterSchema => new LogicVectorParameterValue([LogicValue.X]),
                PositiveUnsigned64ParameterSchema => new Unsigned64ParameterValue(0),
                _ => throw new InvalidOperationException("Every parameter kind needs an invalid-value fixture."),
            };
            foreach (var value in new[] { wrongKind, invalidValue })
            {
                invalid.Add([.. valid.Select(binding => binding.ParameterId == parameter.Id ? new(parameter.Id, value) : binding)]);
            }
        }
        if (valid.Length > 1)
        {
            invalid.Add([.. valid.Reverse()]);
        }
        foreach (var parameters in invalid)
        {
            var outcome = ProjectEditor.Apply(revision, new PlaceComponentInstanceIntent(definition.Id,
                component.Target, parameters, component.Placement));
            var rejected = (await Assert.That(outcome).IsTypeOf<EditRejected>())!;
            await Assert.That(rejected.Diagnostics.Select(diagnostic => diagnostic.Code))
                .Contains("authoring_invalid_parameter");
            await Assert.That(revision.Document.EntryCircuitDefinition.ComponentInstances.Select(instance => instance.Id))
                .IsEquivalentTo([component.Id]);
            await Assert.That(revision.Document.EntryCircuitDefinition.ComponentInstances.Single().Parameters)
                .IsEquivalentTo(valid, CollectionOrdering.Matching);
        }
    }
}
