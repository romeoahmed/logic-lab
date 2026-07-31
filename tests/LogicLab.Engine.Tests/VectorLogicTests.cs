using FsCheck;
using FsCheck.Fluent;
using LogicLab.Domain;
using TUnit.Assertions.Enums;
using TUnit.FsCheck;

namespace LogicLab.Engine.Tests;

public sealed class VectorLogicTests
{
    [Test, FsCheckProperty]
    public Property NormalizeInput_ArbitraryPositiveWidth_MatchesScalarOracleAtEveryBit()
    {
        return CheckUnary(ScalarLogic.NormalizeInput, VectorLogic.NormalizeInput);
    }

    [Test, FsCheckProperty]
    public Property Not_ArbitraryPositiveWidth_MatchesScalarOracleAtEveryBit()
    {
        return CheckUnary(ScalarLogic.Not, VectorLogic.Not);
    }

    [Test, FsCheckProperty]
    public Property And_ArbitraryPositiveWidth_MatchesScalarOracleAtEveryBit()
    {
        return CheckBinary(ScalarLogic.And, VectorLogic.And);
    }

    [Test, FsCheckProperty]
    public Property Or_ArbitraryPositiveWidth_MatchesScalarOracleAtEveryBit()
    {
        return CheckBinary(ScalarLogic.Or, VectorLogic.Or);
    }

    [Test, FsCheckProperty]
    public Property Xor_ArbitraryPositiveWidth_MatchesScalarOracleAtEveryBit()
    {
        return CheckBinary(ScalarLogic.Xor, VectorLogic.Xor);
    }

    [Test]
    [MatrixDataSource]
    public async Task BinaryOperations_WordTailWidthsAndUniformInputs_MatchScalarOracle(
        [Matrix(63, 64, 65, 127, 128, 129, 130)] int width,
        [Matrix(LogicValue.Zero, LogicValue.One, LogicValue.X, LogicValue.Z)]
        LogicValue left,
        [Matrix(LogicValue.Zero, LogicValue.One, LogicValue.X, LogicValue.Z)]
        LogicValue right)
    {
        var leftValues = Enumerable.Repeat(left, width).ToArray();
        var rightValues = Enumerable.Repeat(right, width).ToArray();
        var leftVector = new LogicVector(leftValues);
        var rightVector = new LogicVector(rightValues);

        using (Assert.Multiple())
        {
            await AssertMatchesScalar(
                VectorLogic.And(leftVector, rightVector),
                leftValues,
                rightValues,
                ScalarLogic.And);
            await AssertMatchesScalar(
                VectorLogic.Or(leftVector, rightVector),
                leftValues,
                rightValues,
                ScalarLogic.Or);
            await AssertMatchesScalar(
                VectorLogic.Xor(leftVector, rightVector),
                leftValues,
                rightValues,
                ScalarLogic.Xor);
        }
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
            await Assert.That(() => VectorLogic.Xor(vector, null!))
                .ThrowsExactly<ArgumentNullException>();
        }
    }

    private static Property CheckUnary(
        Func<LogicValue, LogicValue> scalarOperation,
        Func<LogicVector, LogicVector> vectorOperation)
    {
        return Prop.ForAll<int[]>(data =>
        {
            var seed = data is { Length: > 0 } ? data[0] : 0;
            var width = LogicVectorTestData.PositiveWidth(seed);
            var values = LogicVectorTestData.CreateValues(width, seed, data);
            var expected = values.Select(scalarOperation).ToArray();

            return LogicVectorTestData.Matches(
                vectorOperation(new LogicVector(values)),
                expected);
        });
    }

    private static Property CheckBinary(
        Func<LogicValue, LogicValue, LogicValue> scalarOperation,
        Func<LogicVector, LogicVector, LogicVector> vectorOperation)
    {
        return Prop.ForAll<int[], int[]>((leftData, rightData) =>
        {
            var leftSeed = leftData is { Length: > 0 } ? leftData[0] : 0;
            var rightSeed = rightData is { Length: > 0 } ? rightData[0] : 0;
            var width = LogicVectorTestData.PositiveWidth(
                leftSeed ^ rightSeed);
            var leftValues = LogicVectorTestData.CreateValues(
                width,
                leftSeed,
                leftData);
            var rightValues = LogicVectorTestData.CreateValues(
                width,
                rightSeed,
                rightData);
            var expected = Enumerable.Range(0, width)
                .Select(index => scalarOperation(leftValues[index], rightValues[index]))
                .ToArray();

            return LogicVectorTestData.Matches(
                vectorOperation(
                    new LogicVector(leftValues),
                    new LogicVector(rightValues)),
                expected);
        });
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
