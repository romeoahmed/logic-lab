using System.Globalization;
using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.Web.Components.Editor;

namespace LogicLab.Web.Tests;

internal sealed class ParameterFieldTests
{
    [Test]
    [Arguments("en-US")]
    [Arguments("zh-CN")]
    [Arguments("ar-EG")]
    public async Task Parse_AllParameterKinds_PreservesExactValuesAndBitOrder(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            var revision = WebTestCircuit.Commit(ProjectEditor.Apply(WebTestCircuit.CreateCompleteCircuit(),
                new CreateMemoryImageIntent("Boot image", 1, 1, [new MemoryImageWord([LogicValue.X])])));
            var image = revision.Document.MemoryImages.Single();
            (string Contract, string Parameter, ComponentParameterValue Value, string Text)[] cases =
            [
                ("logic.not", "width", new Unsigned32ParameterValue(uint.MaxValue), "4294967295"),
                ("source.clock", "highDuration", new Unsigned64ParameterValue(ulong.MaxValue), "18446744073709551615"),
                ("source.clock", "initialValue", new LogicVectorParameterValue([LogicValue.One]), "1"),
                ("sequential.sr_latch", "initialState", new LogicVectorParameterValue([LogicValue.X]), "X"),
                ("source.input", "initialValue", new LogicVectorParameterValue([LogicValue.Zero, LogicValue.One, LogicValue.X, LogicValue.Z]), "ZX10"),
                ("sink.output", "radix", new ChoiceParameterValue("hex"), "hex"),
                ("topology.concat", "inputWidths", new WidthsParameterValue([2, 4]), "2, 4"),
                ("topology.split", "slices", new SlicesParameterValue([new(0, 2), new(2, 4)]), "0:2, 2:4"),
                ("memory.rom", "initialImage", new MemoryImageParameterValue(image.Id), image.Id.Value),
            ];
            foreach (var item in cases)
            {
                var draft = new ParameterField(Schema(item.Contract, item.Parameter), item.Value);
                await Assert.That(draft.Text).IsEqualTo(item.Text);
                await Assert.That(draft.Parse(revision.Document)).IsEqualTo(item.Value);
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Test]
    [Arguments("logic.not", "width", "4294967296")]
    [Arguments("logic.not", "width", "-1")]
    [Arguments("source.clock", "highDuration", "18446744073709551616")]
    [Arguments("source.clock", "initialValue", "X")]
    [Arguments("source.input", "initialValue", "102")]
    [Arguments("sink.output", "radix", "octal")]
    [Arguments("topology.concat", "inputWidths", "1,,2")]
    [Arguments("topology.split", "slices", "0:2:3")]
    [Arguments("memory.rom", "initialImage", "missing-image")]
    public async Task Parse_InvalidSyntaxOrUnknownChoice_ReturnsNoBinding(string contract, string parameter, string text)
    {
        var draft = new ParameterField(Schema(contract, parameter), new Unsigned32ParameterValue(1)) { Text = text };
        await Assert.That(draft.Parse(WebTestCircuit.CreateCompleteCircuit().Document)).IsNull();
    }

    private static ComponentParameterSchema Schema(string contract, string parameter) =>
        LibrarySnapshot.Core.ResolveContract(new(LibrarySnapshot.Core.LibraryId, contract))!.Parameters
            .Single(item => item.Id == parameter);
}
