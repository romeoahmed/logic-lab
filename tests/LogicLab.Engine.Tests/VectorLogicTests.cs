using FsCheck;
using FsCheck.Fluent;
using LogicLab.Domain;

namespace LogicLab.Engine.Tests;

public sealed class VectorLogicTests
{
    [Fact]
    public void NormalizeInput_ArbitraryPositiveWidth_MatchesScalarOracleAtEveryBit()
    {
        CheckUnary(ScalarLogic.NormalizeInput, VectorLogic.NormalizeInput);
    }

    [Fact]
    public void Not_ArbitraryPositiveWidth_MatchesScalarOracleAtEveryBit()
    {
        CheckUnary(ScalarLogic.Not, VectorLogic.Not);
    }

    [Fact]
    public void And_ArbitraryPositiveWidth_MatchesScalarOracleAtEveryBit()
    {
        CheckBinary(ScalarLogic.And, VectorLogic.And);
    }

    [Fact]
    public void Or_ArbitraryPositiveWidth_MatchesScalarOracleAtEveryBit()
    {
        CheckBinary(ScalarLogic.Or, VectorLogic.Or);
    }

    [Fact]
    public void Xor_ArbitraryPositiveWidth_MatchesScalarOracleAtEveryBit()
    {
        CheckBinary(ScalarLogic.Xor, VectorLogic.Xor);
    }

    [Fact]
    public void GateOperations_XAndZAtWordTails_MatchScalarOracleWithoutLeakage()
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

        AssertMatchesScalar(VectorLogic.NormalizeInput(left), leftValues, ScalarLogic.NormalizeInput);
        AssertMatchesScalar(VectorLogic.Not(left), leftValues, ScalarLogic.Not);
        AssertMatchesScalar(VectorLogic.And(left, right), leftValues, rightValues, ScalarLogic.And);
        AssertMatchesScalar(VectorLogic.Or(left, right), leftValues, rightValues, ScalarLogic.Or);
        AssertMatchesScalar(VectorLogic.Xor(left, right), leftValues, rightValues, ScalarLogic.Xor);
    }

    [Fact]
    public void BinaryOperations_DifferentWidths_ThrowArgumentException()
    {
        var shorter = new LogicVector([LogicValue.Zero]);
        var longer = new LogicVector([LogicValue.Zero, LogicValue.One]);

        Assert.Throws<ArgumentException>(() => VectorLogic.And(shorter, longer));
        Assert.Throws<ArgumentException>(() => VectorLogic.Or(shorter, longer));
        Assert.Throws<ArgumentException>(() => VectorLogic.Xor(shorter, longer));
    }

    [Fact]
    public void Operations_NullOperands_ThrowArgumentNullException()
    {
        var vector = new LogicVector([LogicValue.Zero]);

        Assert.Throws<ArgumentNullException>(
            () => VectorLogic.NormalizeInput(null!));
        Assert.Throws<ArgumentNullException>(() => VectorLogic.Not(null!));
        Assert.Throws<ArgumentNullException>(
            () => VectorLogic.And(null!, vector));
        Assert.Throws<ArgumentNullException>(
            () => VectorLogic.And(vector, null!));
        Assert.Throws<ArgumentNullException>(
            () => VectorLogic.Or(null!, vector));
        Assert.Throws<ArgumentNullException>(
            () => VectorLogic.Xor(vector, null!));
    }

    private static void CheckUnary(
        Func<LogicValue, LogicValue> scalarOperation,
        Func<LogicVector, LogicVector> vectorOperation)
    {
        Prop.ForAll<int[]>(data =>
            {
                var seed = data is { Length: > 0 } ? data[0] : 0;
                var width = LogicVectorTestData.PositiveWidth(seed);
                var values = LogicVectorTestData.CreateValues(width, seed, data);
                var expected = values.Select(scalarOperation).ToArray();

                return LogicVectorTestData.Matches(
                    vectorOperation(new LogicVector(values)),
                    expected);
            })
            .QuickCheckThrowOnFailure();
    }

    private static void CheckBinary(
        Func<LogicValue, LogicValue, LogicValue> scalarOperation,
        Func<LogicVector, LogicVector, LogicVector> vectorOperation)
    {
        CheckBinaryWordTailMatrix(scalarOperation, vectorOperation);

        Prop.ForAll<int[], int[]>((leftData, rightData) =>
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
            })
            .QuickCheckThrowOnFailure();
    }

    private static void CheckBinaryWordTailMatrix(
        Func<LogicValue, LogicValue, LogicValue> scalarOperation,
        Func<LogicVector, LogicVector, LogicVector> vectorOperation)
    {
        int[] wordTailWidths = [63, 64, 65, 127, 128, 129, 130];
        LogicValue[] logicValues =
        [
            LogicValue.Zero,
            LogicValue.One,
            LogicValue.X,
            LogicValue.Z,
        ];

        foreach (var width in wordTailWidths)
        {
            foreach (var left in logicValues)
            {
                foreach (var right in logicValues)
                {
                    var leftValues = Enumerable.Repeat(left, width).ToArray();
                    var rightValues = Enumerable.Repeat(right, width).ToArray();

                    AssertMatchesScalar(
                        vectorOperation(
                            new LogicVector(leftValues),
                            new LogicVector(rightValues)),
                        leftValues,
                        rightValues,
                        scalarOperation);
                }
            }
        }
    }

    private static void AssertMatchesScalar(
        LogicVector actual,
        LogicValue[] values,
        Func<LogicValue, LogicValue> scalarOperation)
    {
        Assert.Equal(values.Length, actual.Width);
        for (var index = 0; index < values.Length; index++)
        {
            Assert.Equal(scalarOperation(values[index]), actual[index]);
        }
    }

    private static void AssertMatchesScalar(
        LogicVector actual,
        LogicValue[] left,
        LogicValue[] right,
        Func<LogicValue, LogicValue, LogicValue> scalarOperation)
    {
        Assert.Equal(left.Length, actual.Width);
        for (var index = 0; index < left.Length; index++)
        {
            Assert.Equal(
                scalarOperation(left[index], right[index]),
                actual[index]);
        }
    }
}
