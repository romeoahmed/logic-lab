using FsCheck;
using FsCheck.Fluent;
using LogicLab.Domain;
using TUnit.Assertions.Enums;
using TUnit.FsCheck;

namespace LogicLab.Engine.Tests;

internal sealed class VectorLogicTests
{
    [Test, FsCheckProperty(Arbitrary = new[] { typeof(LogicVectorArbitraries) })]
    public Property NormalizeInput_ValidVector_MatchesScalarOracleAtEveryBit(
        LogicVectorCase sample)
    {
        return CheckUnary(
            sample,
            ScalarLogic.NormalizeInput,
            VectorLogic.NormalizeInput);
    }

    [Test, FsCheckProperty(Arbitrary = new[] { typeof(LogicVectorArbitraries) })]
    public Property Not_ValidVector_MatchesScalarOracleAtEveryBit(
        LogicVectorCase sample)
    {
        return CheckUnary(sample, ScalarLogic.Not, VectorLogic.Not);
    }

    [Test, FsCheckProperty(Arbitrary = new[] { typeof(LogicVectorArbitraries) })]
    public Property And_ValidSameWidthVectors_MatchesScalarOracleAtEveryBit(
        LogicVectorPairCase sample)
    {
        return CheckBinary(sample, ScalarLogic.And, VectorLogic.And);
    }

    [Test, FsCheckProperty(Arbitrary = new[] { typeof(LogicVectorArbitraries) })]
    public Property Or_ValidSameWidthVectors_MatchesScalarOracleAtEveryBit(
        LogicVectorPairCase sample)
    {
        return CheckBinary(sample, ScalarLogic.Or, VectorLogic.Or);
    }

    [Test, FsCheckProperty(Arbitrary = new[] { typeof(LogicVectorArbitraries) })]
    public Property Xor_ValidSameWidthVectors_MatchesScalarOracleAtEveryBit(
        LogicVectorPairCase sample)
    {
        return CheckBinary(sample, ScalarLogic.Xor, VectorLogic.Xor);
    }

    [Test]
    public async Task GateOperations_XAndZAtWordTails_MatchScalarOracleWithoutLeakage()
    {
        const int width = 130;
        var leftValues = Enumerable.Repeat(LogicValue.Zero, width).ToArray();
        var rightValues = Enumerable.Repeat(LogicValue.One, width).ToArray();
        leftValues[63] = LogicValue.X;
        leftValues[64] = LogicValue.Z;
        leftValues[129] = LogicValue.Z;
        rightValues[63] = LogicValue.Z;
        rightValues[64] = LogicValue.X;
        rightValues[129] = LogicValue.X;
        var left = new LogicVector(leftValues);
        var right = new LogicVector(rightValues);

        using (Assert.Multiple())
        {
            await AssertMatchesScalar(
                VectorLogic.NormalizeInput(left),
                leftValues,
                ScalarLogic.NormalizeInput);
            await AssertMatchesScalar(VectorLogic.Not(left), leftValues, ScalarLogic.Not);
            await AssertMatchesScalar(
                VectorLogic.And(left, right),
                leftValues,
                rightValues,
                ScalarLogic.And);
            await AssertMatchesScalar(
                VectorLogic.Or(left, right),
                leftValues,
                rightValues,
                ScalarLogic.Or);
            await AssertMatchesScalar(
                VectorLogic.Xor(left, right),
                leftValues,
                rightValues,
                ScalarLogic.Xor);
        }
    }

    [Test]
    public async Task BinaryOperations_DifferentWidths_ThrowArgumentException()
    {
        var shorter = new LogicVector([LogicValue.Zero]);
        var longer = new LogicVector([LogicValue.Zero, LogicValue.One]);

        using (Assert.Multiple())
        {
            await Assert.That(() => VectorLogic.And(shorter, longer))
                .ThrowsExactly<ArgumentException>();
            await Assert.That(() => VectorLogic.Or(shorter, longer))
                .ThrowsExactly<ArgumentException>();
            await Assert.That(() => VectorLogic.Xor(shorter, longer))
                .ThrowsExactly<ArgumentException>();
        }
    }

    [Test]
    public async Task Operations_NullOperands_ThrowArgumentNullException()
    {
        var vector = new LogicVector([LogicValue.Zero]);

        using (Assert.Multiple())
        {
            await Assert.That(() => VectorLogic.NormalizeInput(null!))
                .ThrowsExactly<ArgumentNullException>();
            await Assert.That(() => VectorLogic.Not(null!))
                .ThrowsExactly<ArgumentNullException>();
            await Assert.That(() => VectorLogic.And(null!, vector))
                .ThrowsExactly<ArgumentNullException>();
            await Assert.That(() => VectorLogic.And(vector, null!))
                .ThrowsExactly<ArgumentNullException>();
            await Assert.That(() => VectorLogic.Or(null!, vector))
                .ThrowsExactly<ArgumentNullException>();
            await Assert.That(() => VectorLogic.Or(vector, null!))
                .ThrowsExactly<ArgumentNullException>();
            await Assert.That(() => VectorLogic.Xor(null!, vector))
                .ThrowsExactly<ArgumentNullException>();
            await Assert.That(() => VectorLogic.Xor(vector, null!))
                .ThrowsExactly<ArgumentNullException>();
        }
    }

    [Test, FsCheckProperty(Arbitrary = new[] { typeof(LogicVectorArbitraries) })]
    public Property Concat_ValidInputs_MatchesNormalizedScalarProjection(
        LogicVectorSetCase sample)
    {
        var expected = sample.Vectors
            .SelectMany(static values => values)
            .Select(ScalarLogic.NormalizeInput)
            .ToArray();
        var actual = VectorLogic.Concat(
            [.. sample.Vectors.Select(static values => new LogicVector(values))]);
        var matches = LogicVectorTestData.Matches(actual, expected);

        return matches
            .Label(LogicVectorTestData.MismatchLabel(actual, expected))
            .Collect($"inputs={sample.Vectors.Length}")
            .Collect(LogicVectorTestData.WidthBucket(sample.Width));
    }

    [Test, FsCheckProperty(Arbitrary = new[] { typeof(LogicVectorArbitraries) })]
    public Property Extend_ValidOutputWidth_MatchesScalarFillRules(
        LogicVectorCase sample,
        PositiveInt extraWidth)
    {
        var normalized = sample.Values.Select(ScalarLogic.NormalizeInput).ToArray();
        var zeroExpected = normalized
            .Concat(Enumerable.Repeat(LogicValue.Zero, extraWidth.Get))
            .ToArray();
        var signExpected = normalized
            .Concat(Enumerable.Repeat(normalized[^1], extraWidth.Get))
            .ToArray();
        var input = new LogicVector(sample.Values);
        var outputWidth = checked(sample.Width + extraWidth.Get);
        var zeroExtended = VectorLogic.ZeroExtend(input, outputWidth);
        var signExtended = VectorLogic.SignExtend(input, outputWidth);
        var matches = LogicVectorTestData.Matches(zeroExtended, zeroExpected)
            && LogicVectorTestData.Matches(signExtended, signExpected);

        return matches
            .Label(
                $"zero: {LogicVectorTestData.MismatchLabel(zeroExtended, zeroExpected)}; "
                + $"sign: {LogicVectorTestData.MismatchLabel(signExtended, signExpected)}")
            .Collect(LogicVectorTestData.WidthBucket(sample.Width))
            .Collect($"extra={LogicVectorTestData.WidthBucket(extraWidth.Get)}");
    }

    [Test]
    public async Task Concat_NonWordAlignedInputs_PreservesOrderAndNormalizesHighImpedance()
    {
        var firstValues = Enumerable.Repeat(LogicValue.One, 65).ToArray();
        var secondValues = Enumerable.Repeat(LogicValue.Zero, 64).ToArray();
        firstValues[63] = LogicValue.Z;
        secondValues[0] = LogicValue.X;
        secondValues[63] = LogicValue.Z;
        var expected = firstValues
            .Concat(secondValues)
            .Select(ScalarLogic.NormalizeInput)
            .ToArray();

        var actual = VectorLogic.Concat(
            [new LogicVector(firstValues), new LogicVector(secondValues)]);

        using (Assert.Multiple())
        {
            await Assert.That(actual.Width).IsEqualTo(129);
            await Assert.That(LogicVectorTestData.ToValues(actual))
                .IsEquivalentTo(expected, CollectionOrdering.Matching);
        }
    }

    [Test]
    public async Task Extend_HighImpedanceInput_NormalizesAndUsesExactFillRule()
    {
        var input = new LogicVector(
            [LogicValue.One, LogicValue.Z, LogicValue.X]);

        var zeroExtended = VectorLogic.ZeroExtend(input, 5);
        var signExtended = VectorLogic.SignExtend(input, 5);

        using (Assert.Multiple())
        {
            await Assert.That(LogicVectorTestData.ToValues(zeroExtended))
                .IsEquivalentTo(
                    [
                        LogicValue.One,
                        LogicValue.X,
                        LogicValue.X,
                        LogicValue.Zero,
                        LogicValue.Zero,
                    ],
                    CollectionOrdering.Matching);
            await Assert.That(LogicVectorTestData.ToValues(signExtended))
                .IsEquivalentTo(
                    [
                        LogicValue.One,
                        LogicValue.X,
                        LogicValue.X,
                        LogicValue.X,
                        LogicValue.X,
                    ],
                    CollectionOrdering.Matching);
        }
    }

    [Test]
    public async Task Concat_InvalidInputs_ThrowExactExceptions()
    {
        using (Assert.Multiple())
        {
            await Assert.That(() => VectorLogic.Concat(null!))
                .ThrowsExactly<ArgumentNullException>();
            await Assert.That(() => VectorLogic.Concat([]))
                .ThrowsExactly<ArgumentException>();
        }
    }

    [Test]
    public async Task Extend_NullInputs_ThrowExactException()
    {
        using (Assert.Multiple())
        {
            await Assert.That(() => VectorLogic.ZeroExtend(null!, 3))
                .ThrowsExactly<ArgumentNullException>();
            await Assert.That(() => VectorLogic.SignExtend(null!, 3))
                .ThrowsExactly<ArgumentNullException>();
        }
    }

    [Test]
    public async Task Extend_NonIncreasingOutputWidths_ThrowExactException()
    {
        var input = new LogicVector([LogicValue.Zero, LogicValue.One]);

        using (Assert.Multiple())
        {
            await Assert.That(() => VectorLogic.ZeroExtend(input, 2))
                .ThrowsExactly<ArgumentOutOfRangeException>();
            await Assert.That(() => VectorLogic.SignExtend(input, 1))
                .ThrowsExactly<ArgumentOutOfRangeException>();
        }
    }

    private static Property CheckUnary(
        LogicVectorCase sample,
        Func<LogicValue, LogicValue> scalarOperation,
        Func<LogicVector, LogicVector> vectorOperation)
    {
        var expected = sample.Values.Select(scalarOperation).ToArray();
        var actual = vectorOperation(new LogicVector(sample.Values));
        var matches = LogicVectorTestData.Matches(actual, expected);

        return matches
            .Label(LogicVectorTestData.MismatchLabel(actual, expected))
            .Collect(LogicVectorTestData.WidthBucket(sample.Width));
    }

    private static Property CheckBinary(
        LogicVectorPairCase sample,
        Func<LogicValue, LogicValue, LogicValue> scalarOperation,
        Func<LogicVector, LogicVector, LogicVector> vectorOperation)
    {
        var expected = Enumerable.Range(0, sample.Width)
            .Select(index => scalarOperation(sample.Left[index], sample.Right[index]))
            .ToArray();
        var actual = vectorOperation(
            new LogicVector(sample.Left),
            new LogicVector(sample.Right));
        var matches = LogicVectorTestData.Matches(actual, expected);

        return matches
            .Label(LogicVectorTestData.MismatchLabel(actual, expected))
            .Collect(LogicVectorTestData.WidthBucket(sample.Width));
    }

    private static async Task AssertMatchesScalar(
        LogicVector actual,
        LogicValue[] values,
        Func<LogicValue, LogicValue> scalarOperation)
    {
        var expected = values.Select(scalarOperation).ToArray();
        var actualValues = LogicVectorTestData.ToValues(actual);

        using (Assert.Multiple())
        {
            await Assert.That(actual.Width).IsEqualTo(values.Length);
            await Assert.That(actualValues)
                .IsEquivalentTo(expected, CollectionOrdering.Matching);
        }
    }

    private static async Task AssertMatchesScalar(
        LogicVector actual,
        LogicValue[] left,
        LogicValue[] right,
        Func<LogicValue, LogicValue, LogicValue> scalarOperation)
    {
        var expected = Enumerable.Range(0, left.Length)
            .Select(index => scalarOperation(left[index], right[index]))
            .ToArray();
        var actualValues = LogicVectorTestData.ToValues(actual);

        using (Assert.Multiple())
        {
            await Assert.That(actual.Width).IsEqualTo(left.Length);
            await Assert.That(actualValues)
                .IsEquivalentTo(expected, CollectionOrdering.Matching);
        }
    }
}
