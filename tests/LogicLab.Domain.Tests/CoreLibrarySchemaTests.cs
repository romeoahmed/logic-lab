using LogicLab.Domain.Components;

namespace LogicLab.Domain.Tests;

public sealed class CoreLibrarySchemaTests
{
    [Fact]
    public void Library_RequiredIdentity_HasExactValues()
    {
        Assert.Equal("logiclab.core", CoreLibrarySchema.LibraryId);
        Assert.Equal("1.0.0", CoreLibrarySchema.Version);
    }

    [Fact]
    public void FindContract_SourceInput_HasExactSchema()
    {
        var sourceInput = FindCoreContract("source.input");

        Assert.Equal(
            new ComponentContractKey("logiclab.core", "source.input"),
            sourceInput.Key);
        Assert.Equal(
            ["width", "initialValue"],
            sourceInput.Parameters.Select(parameter => parameter.Id));
        Assert.Equal(
            [ComponentParameterKind.PositiveWidth, ComponentParameterKind.LogicVector],
            sourceInput.Parameters.Select(parameter => parameter.Kind));
        Assert.Null(sourceInput.Parameters[0].WidthParameterId);
        Assert.Equal("width", sourceInput.Parameters[1].WidthParameterId);
        Assert.Empty(sourceInput.Parameters[0].AllowedValues);
        Assert.Empty(sourceInput.Parameters[1].AllowedValues);
        Assert.Equal(["Q"], sourceInput.Ports.Select(port => port.Id));
        Assert.Equal([PortDirection.Output], sourceInput.Ports.Select(port => port.Direction));
        Assert.Equal(["width"], sourceInput.Ports.Select(port => port.WidthParameterId));
    }

    [Fact]
    public void FindContract_LogicNot_HasExactSchema()
    {
        var logicNot = FindCoreContract("logic.not");

        Assert.Equal(
            new ComponentContractKey("logiclab.core", "logic.not"),
            logicNot.Key);
        Assert.Equal(["width"], logicNot.Parameters.Select(parameter => parameter.Id));
        Assert.Equal(
            [ComponentParameterKind.PositiveWidth],
            logicNot.Parameters.Select(parameter => parameter.Kind));
        Assert.Null(logicNot.Parameters[0].WidthParameterId);
        Assert.Empty(logicNot.Parameters[0].AllowedValues);
        Assert.Equal(["A", "Q"], logicNot.Ports.Select(port => port.Id));
        Assert.Equal(
            [PortDirection.Input, PortDirection.Output],
            logicNot.Ports.Select(port => port.Direction));
        Assert.Equal(
            ["width", "width"],
            logicNot.Ports.Select(port => port.WidthParameterId));
    }

    [Fact]
    public void FindContract_SinkOutput_HasExactSchema()
    {
        var sinkOutput = FindCoreContract("sink.output");

        Assert.Equal(
            new ComponentContractKey("logiclab.core", "sink.output"),
            sinkOutput.Key);
        Assert.Equal(
            ["width", "radix"],
            sinkOutput.Parameters.Select(parameter => parameter.Id));
        Assert.Equal(
            [ComponentParameterKind.PositiveWidth, ComponentParameterKind.Choice],
            sinkOutput.Parameters.Select(parameter => parameter.Kind));
        Assert.Null(sinkOutput.Parameters[0].WidthParameterId);
        Assert.Null(sinkOutput.Parameters[1].WidthParameterId);
        Assert.Empty(sinkOutput.Parameters[0].AllowedValues);
        Assert.Equal(
            ["binary", "hex", "unsigned"],
            sinkOutput.Parameters[1].AllowedValues);
        Assert.Equal(["D"], sinkOutput.Ports.Select(port => port.Id));
        Assert.Equal([PortDirection.Input], sinkOutput.Ports.Select(port => port.Direction));
        Assert.Equal(["width"], sinkOutput.Ports.Select(port => port.WidthParameterId));
    }

    [Fact]
    public void FindContract_UnknownContract_ReturnsNull()
    {
        var contract = CoreLibrarySchema.FindContract(
            new ComponentContractKey("logiclab.core", "logic.unknown"));

        Assert.Null(contract);
    }

    [Fact]
    public void FindContract_UnknownLibrary_ReturnsNull()
    {
        var contract = CoreLibrarySchema.FindContract(
            new ComponentContractKey("other.library", "logic.not"));

        Assert.Null(contract);
    }

    private static ComponentContractSchema FindCoreContract(string contractId)
    {
        return Assert.IsType<ComponentContractSchema>(
            CoreLibrarySchema.FindContract(
                new ComponentContractKey("logiclab.core", contractId)));
    }
}
