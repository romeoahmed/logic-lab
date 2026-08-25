using FsCheck;
using FsCheck.Fluent;
using LogicLab.Domain;

namespace LogicLab.Web.Tests;

internal sealed record SceneLogicVectorTransferCase(LogicValue[] Values)
{
    public override string ToString() =>
        $"width={Values.Length}, values={string.Concat(Values.Select(Token))}";

    private static char Token(LogicValue value) => value switch
    {
        LogicValue.Zero => '0',
        LogicValue.One => '1',
        LogicValue.X => 'X',
        LogicValue.Z => 'Z',
        _ => '?',
    };
}

internal static class SceneRecordArbitraries
{
    private static readonly int[] PackingBoundaryWidths =
    [
        1,
        3,
        4,
        5,
        63,
        64,
        65,
    ];

    public static Arbitrary<SceneLogicVectorTransferCase> LogicVectorTransfer()
    {
        var width = Gen.Frequency(
            (3, Gen.Elements(PackingBoundaryWidths)),
            (7, Gen.Choose(1, 257)));
        var generator =
            from count in width
            from values in Gen.Elements(Enum.GetValues<LogicValue>()).ArrayOf(count)
            select new SceneLogicVectorTransferCase(values);

        return Arb.From(generator, Shrink);
    }

    private static IEnumerable<SceneLogicVectorTransferCase> Shrink(
        SceneLogicVectorTransferCase sample)
    {
        if (sample.Values.Length > 1)
        {
            yield return new SceneLogicVectorTransferCase([sample.Values[0]]);
        }

        if (sample.Values.Any(value => value != LogicValue.Zero))
        {
            yield return new SceneLogicVectorTransferCase(
                [.. Enumerable.Repeat(LogicValue.Zero, sample.Values.Length)]);
        }
    }
}
