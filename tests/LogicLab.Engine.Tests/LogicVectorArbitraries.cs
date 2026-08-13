using FsCheck;
using FsCheck.Fluent;
using LogicLab.Domain;

namespace LogicLab.Engine.Tests;

internal sealed record LogicVectorCase(LogicValue[] Values)
{
    public int Width => Values.Length;

    public override string ToString() => $"Vector(width={Width})";
}

internal sealed record LogicVectorSliceCase(
    LogicValue[] Values,
    int Offset,
    int Length)
{
    public int Width => Values.Length;

    public override string ToString() =>
        $"Slice(width={Width}, offset={Offset}, length={Length})";
}

internal sealed record LogicVectorPairCase(
    LogicValue[] Left,
    LogicValue[] Right)
{
    public int Width => Left.Length;

    public override string ToString() => $"Pair(width={Width})";
}

internal sealed record LogicVectorSetCase(LogicValue[][] Vectors)
{
    public int Width => Vectors[0].Length;

    public override string ToString() =>
        $"VectorSet(width={Width}, vectors={Vectors.Length})";
}

internal sealed record LogicVectorSequenceCase(LogicValue[][] Vectors)
{
    public int TotalWidth => Vectors.Sum(static values => values.Length);

    public override string ToString() =>
        $"VectorSequence(totalWidth={TotalWidth}, vectors={Vectors.Length})";
}

internal sealed record LogicVectorDriverCase(
    int Width,
    LogicValue[][] Drivers)
{
    public override string ToString() =>
        $"Drivers(width={Width}, drivers={Drivers.Length})";
}

internal sealed record LogicVectorArithmeticCase(
    LogicValue[] Left,
    LogicValue[] Right,
    LogicValue Control)
{
    public int Width => Left.Length;

    public override string ToString() =>
        $"Arithmetic(width={Width}, control={Control})";
}

internal static class LogicVectorArbitraries
{
    private static readonly int[] BoundaryWidths =
        [1, 63, 64, 65, 127, 128, 129, 130, 255, 256, 257];

    private static readonly Gen<LogicValue> LogicValueGenerator =
        Gen.Elements(Enum.GetValues<LogicValue>());

    private static readonly Gen<int> WidthGenerator = Gen.Frequency(
        (3, Gen.Elements(BoundaryWidths)),
        (7, Gen.Choose(1, 257)));

    public static Arbitrary<LogicVectorCase> LogicVector()
    {
        var generator = WidthGenerator.SelectMany(
            VectorValues,
            (_, values) => new LogicVectorCase(values));

        return Arb.From(generator, ShrinkVector);
    }

    public static Arbitrary<LogicVectorSliceCase> LogicVectorSlice()
    {
        var generator =
            from width in WidthGenerator
            from values in VectorValues(width)
            from range in SliceRange(width)
            select new LogicVectorSliceCase(values, range.Offset, range.Length);

        return Arb.From(generator, ShrinkSlice);
    }

    public static Arbitrary<LogicVectorPairCase> LogicVectorPair()
    {
        var generator =
            from width in WidthGenerator
            from vectors in IndependentVectors(width, 2)
            select new LogicVectorPairCase(vectors[0], vectors[1]);

        return Arb.From(generator, ShrinkPair);
    }

    public static Arbitrary<LogicVectorSetCase> LogicVectorSet()
    {
        var generator =
            from width in WidthGenerator
            from count in Gen.Choose(1, 5)
            from vectors in IndependentVectors(width, count)
            select new LogicVectorSetCase(vectors);

        return Arb.From(generator, ShrinkSet);
    }

    public static Arbitrary<LogicVectorSequenceCase> LogicVectorSequence()
    {
        var generator =
            from count in Gen.Choose(1, 5)
            from vectors in Gen.CollectToArray(Enumerable.Repeat(
                from width in WidthGenerator
                from values in VectorValues(width)
                select values,
                count))
            select new LogicVectorSequenceCase(vectors);

        return Arb.From(generator, ShrinkSequence);
    }

    public static Arbitrary<LogicVectorDriverCase> LogicVectorDrivers()
    {
        var generator =
            from width in WidthGenerator
            from count in Gen.Choose(0, 5)
            from drivers in IndependentVectors(width, count)
            select new LogicVectorDriverCase(width, drivers);

        return Arb.From(generator, ShrinkDrivers);
    }

    public static Arbitrary<LogicVectorArithmeticCase> LogicVectorArithmetic()
    {
        var generator =
            from width in WidthGenerator
            from vectors in IndependentVectors(width, 2)
            from control in LogicValueGenerator
            select new LogicVectorArithmeticCase(
                vectors[0],
                vectors[1],
                control);

        return Arb.From(generator, ShrinkArithmetic);
    }

    private static Gen<LogicValue[]> VectorValues(int width) =>
        Gen.Frequency(
            (7, LogicValueGenerator.ArrayOf(width)),
            (2, LogicValueGenerator.Select(value =>
                Enumerable.Repeat(value, width).ToArray())),
            (1, BoundaryVectorValues(width)));

    private static Gen<LogicValue[]> BoundaryVectorValues(int width)
    {
        var boundaryIndices = new[] { 0, 63, 64, 127, 128, width - 1 }
            .Where(index => index >= 0 && index < width)
            .Distinct()
            .ToArray();

        return
            from baseline in LogicValueGenerator
            from boundaryValues in LogicValueGenerator.ArrayOf(boundaryIndices.Length)
            select CreateBoundaryVector(width, baseline, boundaryIndices, boundaryValues);
    }

    private static LogicValue[] CreateBoundaryVector(
        int width,
        LogicValue baseline,
        int[] boundaryIndices,
        LogicValue[] boundaryValues)
    {
        var values = Enumerable.Repeat(baseline, width).ToArray();
        for (var index = 0; index < boundaryIndices.Length; index++)
        {
            values[boundaryIndices[index]] = boundaryValues[index];
        }

        return values;
    }

    private static Gen<(int Offset, int Length)> SliceRange(int width)
    {
        var randomRange =
            from offset in Gen.Choose(0, width - 1)
            from length in Gen.Choose(1, width - offset)
            select (offset, length);
        var boundaryRanges = new[]
        {
            (Offset: 0, Length: width),
            (Offset: width - 1, Length: 1),
            (Offset: 63, Length: Math.Min(2, width - 63)),
            (Offset: 127, Length: Math.Min(2, width - 127)),
            (Offset: 61, Length: Math.Min(83, width - 61)),
        }
            .Where(range => range.Offset >= 0 && range.Offset < width && range.Length > 0)
            .Distinct()
            .ToArray();

        return Gen.Frequency(
            (7, randomRange),
            (3, Gen.Elements(boundaryRanges)));
    }

    private static Gen<LogicValue[][]> IndependentVectors(int width, int count) =>
        Gen.CollectToArray(Enumerable.Repeat(VectorValues(width), count));

    private static IEnumerable<LogicVectorCase> ShrinkVector(LogicVectorCase sample)
    {
        foreach (var values in ShrinkValues(sample.Values))
        {
            yield return new LogicVectorCase(values);
        }
    }

    private static IEnumerable<LogicVectorSliceCase> ShrinkSlice(
        LogicVectorSliceCase sample)
    {
        if (sample.Offset > 0)
        {
            yield return sample with { Offset = 0 };
        }

        if (sample.Length > 1)
        {
            yield return sample with { Length = 1 };
        }

        var minimumWidth = sample.Offset + sample.Length;
        var smallerWidths = ShrinkWidth(sample.Width)
            .Where(width => width >= minimumWidth)
            .Append(minimumWidth)
            .Where(width => width < sample.Width)
            .Distinct()
            .Order();
        foreach (var width in smallerWidths)
        {
            yield return sample with { Values = sample.Values[..width] };
        }

        foreach (var values in ShrinkLogicValues(sample.Values))
        {
            yield return sample with { Values = values };
        }
    }

    private static IEnumerable<LogicVectorPairCase> ShrinkPair(
        LogicVectorPairCase sample)
    {
        foreach (var width in ShrinkWidth(sample.Width))
        {
            yield return new LogicVectorPairCase(
                sample.Left[..width],
                sample.Right[..width]);
        }

        foreach (var left in ShrinkLogicValues(sample.Left))
        {
            yield return sample with { Left = left };
        }

        foreach (var right in ShrinkLogicValues(sample.Right))
        {
            yield return sample with { Right = right };
        }
    }

    private static IEnumerable<LogicVectorSetCase> ShrinkSet(
        LogicVectorSetCase sample)
    {
        for (var index = 0; index < sample.Vectors.Length && sample.Vectors.Length > 1; index++)
        {
            yield return new LogicVectorSetCase(
                [.. sample.Vectors.Where(
                    (_, candidateIndex) => candidateIndex != index)]);
        }

        foreach (var width in ShrinkWidth(sample.Width))
        {
            yield return new LogicVectorSetCase(
                [.. sample.Vectors.Select(values => values[..width])]);
        }

        foreach (var vectors in ShrinkVectorValues(sample.Vectors))
        {
            yield return new LogicVectorSetCase(vectors);
        }
    }

    private static IEnumerable<LogicVectorSequenceCase> ShrinkSequence(
        LogicVectorSequenceCase sample)
    {
        for (var index = 0; index < sample.Vectors.Length && sample.Vectors.Length > 1; index++)
        {
            yield return new LogicVectorSequenceCase(
                [.. sample.Vectors.Where((_, candidateIndex) => candidateIndex != index)]);
        }

        for (var index = 0; index < sample.Vectors.Length; index++)
        {
            foreach (var values in ShrinkValues(sample.Vectors[index]))
            {
                var candidate = (LogicValue[][])sample.Vectors.Clone();
                candidate[index] = values;
                yield return new LogicVectorSequenceCase(candidate);
            }
        }
    }

    private static IEnumerable<LogicVectorDriverCase> ShrinkDrivers(
        LogicVectorDriverCase sample)
    {
        for (var index = 0; index < sample.Drivers.Length; index++)
        {
            yield return sample with
            {
                Drivers = [.. sample.Drivers.Where((_, candidateIndex) => candidateIndex != index)],
            };
        }

        foreach (var width in ShrinkWidth(sample.Width))
        {
            yield return new LogicVectorDriverCase(
                width,
                [.. sample.Drivers.Select(values => values[..width])]);
        }

        foreach (var drivers in ShrinkVectorValues(sample.Drivers))
        {
            yield return sample with { Drivers = drivers };
        }
    }

    private static IEnumerable<LogicVectorArithmeticCase> ShrinkArithmetic(
        LogicVectorArithmeticCase sample)
    {
        foreach (var width in ShrinkWidth(sample.Width))
        {
            yield return sample with
            {
                Left = sample.Left[..width],
                Right = sample.Right[..width],
            };
        }

        foreach (var values in ShrinkLogicValues(sample.Left))
        {
            yield return sample with { Left = values };
        }

        foreach (var values in ShrinkLogicValues(sample.Right))
        {
            yield return sample with { Right = values };
        }

        if (sample.Control != LogicValue.Zero)
        {
            yield return sample with { Control = LogicValue.Zero };
        }
    }

    private static IEnumerable<LogicValue[]> ShrinkValues(LogicValue[] values)
    {
        foreach (var width in ShrinkWidth(values.Length))
        {
            yield return values[..width];
        }

        foreach (var shrunk in ShrinkLogicValues(values))
        {
            yield return shrunk;
        }
    }

    private static IEnumerable<LogicValue[][]> ShrinkVectorValues(
        LogicValue[][] vectors)
    {
        for (var vectorIndex = 0; vectorIndex < vectors.Length; vectorIndex++)
        {
            foreach (var values in ShrinkLogicValues(vectors[vectorIndex]))
            {
                var candidate = (LogicValue[][])vectors.Clone();
                candidate[vectorIndex] = values;
                yield return candidate;
            }
        }
    }

    private static IEnumerable<LogicValue[]> ShrinkLogicValues(LogicValue[] values)
    {
        for (var index = 0; index < values.Length; index++)
        {
            if (values[index] == LogicValue.Zero)
            {
                continue;
            }

            var candidate = (LogicValue[])values.Clone();
            candidate[index] = LogicValue.Zero;
            yield return candidate;
        }
    }

    private static IEnumerable<int> ShrinkWidth(int width)
    {
        var candidates = BoundaryWidths
            .Where(candidate => candidate < width)
            .Append(Math.Max(1, width / 2))
            .Append(width - 1)
            .Where(candidate => candidate >= 1 && candidate < width)
            .Distinct()
            .Order();

        foreach (var candidate in candidates)
        {
            yield return candidate;
        }
    }
}
