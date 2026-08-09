using FsCheck;
using FsCheck.Fluent;
using LogicLab.Domain.Authoring;

namespace LogicLab.Domain.Tests;

internal sealed record SplitPortCase(uint Width, BitSlice[] Slices)
{
    public override string ToString() =>
        $"Split(width={Width}, slices={string.Join(", ", Slices)})";
}

internal sealed record ConcatPortCase(uint[] Widths)
{
    public override string ToString() =>
        $"Concat(widths={string.Join(", ", Widths)})";
}

internal static class ComponentContractArbitraries
{
    private const int MaximumWidth = 64;
    private const int MaximumPortItemCount = 8;

    public static Arbitrary<SplitPortCase> SplitPorts()
    {
        var generator =
            from width in Gen.Choose(1, MaximumWidth)
            from sliceCount in Gen.Choose(2, MaximumPortItemCount)
            from slices in Gen.CollectToArray(
                Enumerable.Repeat(Slice(width), sliceCount))
            select new SplitPortCase(checked((uint)width), slices);

        return Arb.From(generator, ShrinkSplit);
    }

    public static Arbitrary<ConcatPortCase> ConcatPorts()
    {
        var generator =
            from widthCount in Gen.Choose(2, MaximumPortItemCount)
            from widths in Gen.Choose(1, MaximumWidth).ArrayOf(widthCount)
            select new ConcatPortCase(
                [.. widths.Select(static width => checked((uint)width))]);

        return Arb.From(generator, ShrinkConcat);
    }

    private static Gen<BitSlice> Slice(int width) =>
        from offset in Gen.Choose(0, width - 1)
        from length in Gen.Choose(1, width - offset)
        select new BitSlice(checked((uint)offset), checked((uint)length));

    private static IEnumerable<SplitPortCase> ShrinkSplit(SplitPortCase sample)
    {
        if (sample.Slices.Length > 2)
        {
            for (var index = 0; index < sample.Slices.Length; index++)
            {
                yield return sample with
                {
                    Slices =
                    [.. sample.Slices.Where(
                        (_, candidateIndex) => candidateIndex != index)],
                };
            }
        }

        for (var index = 0; index < sample.Slices.Length; index++)
        {
            var slice = sample.Slices[index];
            if (slice.Offset > 0)
            {
                yield return Replace(
                    sample,
                    index,
                    slice with { Offset = 0 });
            }

            if (slice.Length > 1)
            {
                yield return Replace(
                    sample,
                    index,
                    slice with { Length = 1 });
            }
        }

        var minimumWidth = sample.Slices.Max(static slice => checked(
            slice.Offset + slice.Length));
        if (minimumWidth < sample.Width)
        {
            yield return sample with { Width = minimumWidth };
        }
    }

    private static SplitPortCase Replace(
        SplitPortCase sample,
        int index,
        BitSlice slice)
    {
        var slices = (BitSlice[])sample.Slices.Clone();
        slices[index] = slice;
        return sample with { Slices = slices };
    }

    private static IEnumerable<ConcatPortCase> ShrinkConcat(ConcatPortCase sample)
    {
        if (sample.Widths.Length > 2)
        {
            for (var index = 0; index < sample.Widths.Length; index++)
            {
                yield return sample with
                {
                    Widths =
                    [.. sample.Widths.Where(
                        (_, candidateIndex) => candidateIndex != index)],
                };
            }
        }

        for (var index = 0; index < sample.Widths.Length; index++)
        {
            if (sample.Widths[index] == 1)
            {
                continue;
            }

            var widths = (uint[])sample.Widths.Clone();
            widths[index] = 1;
            yield return sample with { Widths = widths };
        }
    }
}
