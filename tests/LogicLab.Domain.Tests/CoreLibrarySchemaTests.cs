using LogicLab.Domain.Components;
using TUnit.Assertions.Enums;

namespace LogicLab.Domain.Tests;

public sealed class CoreLibrarySchemaTests
{
    [Test]
    public async Task Library_RequiredIdentity_HasExactValues()
    {
        using (Assert.Multiple())
        {
            await Assert.That(CoreLibrarySchema.LibraryId).IsEqualTo("logiclab.core");
            await Assert.That(CoreLibrarySchema.Version).IsEqualTo("1.0.0");
        }
    }

    [Test]
    public async Task FindContract_SourceInput_HasExactSchema()
    {
        var sourceInput = await FindCoreContract("source.input");

        using (Assert.Multiple())
        {
            await Assert.That(sourceInput.Key)
                .IsEqualTo(new ComponentContractKey("logiclab.core", "source.input"));
            await Assert.That(sourceInput.Parameters.Select(parameter => parameter.Id).ToArray())
                .IsEquivalentTo(["width", "initialValue"], CollectionOrdering.Matching);
            await Assert.That(sourceInput.Parameters.Select(parameter => parameter.Kind).ToArray())
                .IsEquivalentTo(
                    [ComponentParameterKind.PositiveWidth, ComponentParameterKind.LogicVector],
                    CollectionOrdering.Matching);
            await Assert.That(sourceInput.Parameters[0].WidthParameterId).IsNull();
            await Assert.That(sourceInput.Parameters[1].WidthParameterId).IsEqualTo("width");
            await Assert.That(sourceInput.Parameters[0].AllowedValues).IsEmpty();
            await Assert.That(sourceInput.Parameters[1].AllowedValues).IsEmpty();
            await Assert.That(sourceInput.Ports.Select(port => port.Id).ToArray())
                .IsEquivalentTo(["Q"], CollectionOrdering.Matching);
            await Assert.That(sourceInput.Ports.Select(port => port.Direction).ToArray())
                .IsEquivalentTo([PortDirection.Output], CollectionOrdering.Matching);
            await Assert.That(sourceInput.Ports.Select(port => port.WidthParameterId).ToArray())
                .IsEquivalentTo(["width"], CollectionOrdering.Matching);
        }
    }

    [Test]
    public async Task FindContract_LogicNot_HasExactSchema()
    {
        var logicNot = await FindCoreContract("logic.not");

        using (Assert.Multiple())
        {
            await Assert.That(logicNot.Key)
                .IsEqualTo(new ComponentContractKey("logiclab.core", "logic.not"));
            await Assert.That(logicNot.Parameters.Select(parameter => parameter.Id).ToArray())
                .IsEquivalentTo(["width"], CollectionOrdering.Matching);
            await Assert.That(logicNot.Parameters.Select(parameter => parameter.Kind).ToArray())
                .IsEquivalentTo(
                    [ComponentParameterKind.PositiveWidth],
                    CollectionOrdering.Matching);
            await Assert.That(logicNot.Parameters[0].WidthParameterId).IsNull();
            await Assert.That(logicNot.Parameters[0].AllowedValues).IsEmpty();
            await Assert.That(logicNot.Ports.Select(port => port.Id).ToArray())
                .IsEquivalentTo(["A", "Q"], CollectionOrdering.Matching);
            await Assert.That(logicNot.Ports.Select(port => port.Direction).ToArray())
                .IsEquivalentTo(
                    [PortDirection.Input, PortDirection.Output],
                    CollectionOrdering.Matching);
            await Assert.That(logicNot.Ports.Select(port => port.WidthParameterId).ToArray())
                .IsEquivalentTo(["width", "width"], CollectionOrdering.Matching);
        }
    }

    [Test]
    public async Task FindContract_SinkOutput_HasExactSchema()
    {
        var sinkOutput = await FindCoreContract("sink.output");

        using (Assert.Multiple())
        {
            await Assert.That(sinkOutput.Key)
                .IsEqualTo(new ComponentContractKey("logiclab.core", "sink.output"));
            await Assert.That(sinkOutput.Parameters.Select(parameter => parameter.Id).ToArray())
                .IsEquivalentTo(["width", "radix"], CollectionOrdering.Matching);
            await Assert.That(sinkOutput.Parameters.Select(parameter => parameter.Kind).ToArray())
                .IsEquivalentTo(
                    [ComponentParameterKind.PositiveWidth, ComponentParameterKind.Choice],
                    CollectionOrdering.Matching);
            await Assert.That(sinkOutput.Parameters[0].WidthParameterId).IsNull();
            await Assert.That(sinkOutput.Parameters[1].WidthParameterId).IsNull();
            await Assert.That(sinkOutput.Parameters[0].AllowedValues).IsEmpty();
            await Assert.That(sinkOutput.Parameters[1].AllowedValues)
                .IsEquivalentTo(
                    ["binary", "hex", "unsigned"],
                    CollectionOrdering.Matching);
            await Assert.That(sinkOutput.Ports.Select(port => port.Id).ToArray())
                .IsEquivalentTo(["D"], CollectionOrdering.Matching);
            await Assert.That(sinkOutput.Ports.Select(port => port.Direction).ToArray())
                .IsEquivalentTo([PortDirection.Input], CollectionOrdering.Matching);
            await Assert.That(sinkOutput.Ports.Select(port => port.WidthParameterId).ToArray())
                .IsEquivalentTo(["width"], CollectionOrdering.Matching);
        }
    }

    [Test]
    [Arguments("logiclab.core", "logic.unknown")]
    [Arguments("other.library", "logic.not")]
    public async Task FindContract_UnknownKey_ReturnsNull(
        string libraryId,
        string contractId)
    {
        var contract = CoreLibrarySchema.FindContract(
            new ComponentContractKey(libraryId, contractId));

        await Assert.That(contract).IsNull();
    }

    private static async Task<ComponentContractSchema> FindCoreContract(string contractId)
    {
        var contract = CoreLibrarySchema.FindContract(
            new ComponentContractKey("logiclab.core", contractId));

        var schema = await Assert.That(contract).IsTypeOf<ComponentContractSchema>();
        Assert.NotNull(schema);
        return schema;
    }
}
