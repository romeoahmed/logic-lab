using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;

namespace LogicLab.Domain.Tests;

internal static class ProjectEditorTestContext
{
    public static ProjectRevision Commit(EditOutcome outcome) =>
        ((EditCommitted)outcome).Revision;

    public static ComponentContractKey Contract(string contractId) =>
        new(CoreLibrarySchema.LibraryId, contractId);

    public static SymbolProfileReference TeachingMixedProfile() =>
        new(
            "TeachingMixed",
            "1.0.0",
            IndicationConvention.Negation);

    public static ComponentParameterBinding[] WidthParameters(uint width) =>
        [new ComponentParameterBinding("width", new Unsigned32ParameterValue(width))];

    public static ComponentParameterBinding[] ConstantParameters(
        params LogicValue[] values) =>
        [
            new ComponentParameterBinding(
                "width",
                new Unsigned32ParameterValue(checked((uint)values.Length))),
            new ComponentParameterBinding("value", new LogicVectorParameterValue(values)),
        ];

    public static ComponentParameterBinding[] SinkParameters(uint width) =>
        [
            new ComponentParameterBinding("width", new Unsigned32ParameterValue(width)),
            new ComponentParameterBinding("radix", new ChoiceParameterValue("binary")),
        ];
}
