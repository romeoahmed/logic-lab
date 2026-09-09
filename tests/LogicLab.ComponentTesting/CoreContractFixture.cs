using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;

namespace LogicLab.ComponentTesting;

internal static class CoreContractFixture
{
    public static ProjectRevision CreateRevision(string contractId, uint dataWidth = 3)
    {
        var revision = ((ProjectGenesisCommitted)ProjectEditor.Begin(new NewProjectSeed("Contract fixture", LibrarySnapshot.Core,
            new SymbolProfileReference("TeachingMixed", "1.0.0", IndicationConvention.Negation), "Main"))).Revision;
        var contract = LibrarySnapshot.Core.Contracts.Single(item => item.Key.ContractId == contractId);
        var parameters = new List<ComponentParameterBinding>();
        uint Width(string id) => ((Unsigned32ParameterValue)parameters.Single(item => item.ParameterId == id).Value).Value;
        foreach (var schema in contract.Parameters)
        {
            ComponentParameterValue value;
            if (schema is MemoryImageParameterSchema memory)
            {
                var width = Width(memory.WordWidthParameterId);
                var depth = 1u << checked((int)Width(memory.AddressWidthParameterId));
                revision = Commit(ProjectEditor.Apply(revision, new CreateMemoryImageIntent("Initial data", width, depth,
                    [.. Enumerable.Range(0, checked((int)depth)).Select(address => new MemoryImageWord(
                        [.. Enumerable.Range(0, checked((int)width)).Select(bit => (LogicValue)((address + bit) % 3))]))])));
                value = new MemoryImageParameterValue(revision.Document.MemoryImages.Single().Id);
            }
            else
            {
                value = schema switch
                {
                    WidthParameterSchema width => new Unsigned32ParameterValue(Math.Max(width.MinimumValue,
                        width.GreaterThanParameterId is { } input ? Width(input) + (dataWidth == 1 ? 1u : 2u)
                        : width.Id is "selectorWidth" or "addressWidth" or "fanIn" or "inputCount" ? Math.Min(dataWidth, 3) : dataWidth)),
                    FixedLogicVectorParameterSchema vector => Vector(vector.Width),
                    VariableLogicVectorParameterSchema vector => Vector(Width(vector.WidthParameterId)),
                    BinaryLogicParameterSchema => new LogicVectorParameterValue([dataWidth == 1 ? LogicValue.Zero : LogicValue.One]),
                    PositiveUnsigned64ParameterSchema => new Unsigned64ParameterValue(dataWidth == 1 ? 1UL : 9_007_199_254_740_993),
                    ChoiceParameterSchema choice => new ChoiceParameterValue(dataWidth == 1 ? choice.AllowedValues[0] : choice.AllowedValues[^1]),
                    SlicesParameterSchema slices => new SlicesParameterValue(
                        [.. Enumerable.Range(0, slices.MinimumItemCount).Select(index => new BitSlice(dataWidth == 1 ? 0 : checked((uint)index), 1))]),
                    WidthsParameterSchema widths => new WidthsParameterValue(
                        [.. Enumerable.Range(0, widths.MinimumItemCount).Select(index => dataWidth == 1 ? 1 : checked((uint)index + (dataWidth == 3 ? 2 : dataWidth)))]),
                    _ => throw new InvalidOperationException("The fixture must cover every parameter schema."),
                };
            }
            parameters.Add(new(schema.Id, value));
        }
        return Commit(ProjectEditor.Apply(revision, new PlaceComponentInstanceIntent(revision.Document.EntryCircuitDefinitionId,
            new LibraryComponentTarget(contract.Key), parameters,
            new ComponentPlacement(new GridPoint(-3, 7), QuarterTurn.Three, true), "Serialized component")));
    }

    private static LogicVectorParameterValue Vector(uint width) => new(
        [.. Enumerable.Range(0, checked((int)width)).Select(index => (LogicValue)(index % 3))]);

    private static ProjectRevision Commit(EditOutcome outcome) => ((EditCommitted)outcome).Revision;
}
