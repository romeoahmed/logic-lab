namespace LogicLab.Presentation.Tests;

internal static class SequentialAndMemoryPresentationTestData
{
    public static IEnumerable<Func<Item25RecipeExpectation>> Item25RecipeExpectations()
    {
        yield return () => new Item25RecipeExpectation(
            "sequential.d_latch",
            ["1D", "C1"],
            ["3.3-13", "4.3.7", "5.9"],
            ["Q"]);
        yield return () => new Item25RecipeExpectation(
            "sequential.dff",
            ["1D", "C1"],
            ["3.3-13", "4.3.7", "5.9", "3.1-9"],
            ["Q"]);
        yield return () => new Item25RecipeExpectation(
            "sequential.jkff",
            ["1J", "1K", "C1"],
            ["3.3-14", "3.3-15", "4.3.7", "5.9", "3.1-9", "3.1.1", "3.1-2"],
            ["Q", "QN"]);
        yield return () => new Item25RecipeExpectation(
            "sequential.tff",
            ["1T", "C1"],
            ["3.3-18", "4.3.7", "5.9", "3.1-9", "3.1.1", "3.1-2"],
            ["Q", "QN"]);
        yield return () => new Item25RecipeExpectation(
            "sequential.register",
            ["1,2D", "C1", "EN2"],
            ["3.3-13", "4.3.7", "4.3.9", "5.9", "3.1-9"],
            ["Q"]);
        yield return () => new Item25RecipeExpectation(
            "sequential.shift_register",
            ["1,2D", "¬1,2,3D", "M1", "C2/¬1,3→", "EN3"],
            ["3.3-13", "3.3-19", "4.3.1", "4.3.7", "4.3.9", "4.4.3", "5.13-1", "3.1-9"],
            ["PARALLEL", "SERIAL", "Q", "SERIAL_OUT"]);
        yield return () => new Item25RecipeExpectation(
            "sequential.counter",
            ["1,2D", "M1", "C2/¬1,3+", "EN3"],
            ["3.3-13", "3.3-21", "3.3-36", "4.3.1", "4.3.7", "4.3.9", "4.4.3", "5.13-1", "5.13-17", "3.1-9"],
            ["LOAD_VALUE", "Q", "TERMINAL"]);
        yield return () => new Item25RecipeExpectation(
            "memory.rom",
            ["A0/1", "A"],
            ["3.3-25", "4.3.11", "4.4.2", "5.14-1"],
            ["Q"]);
        yield return () => new Item25RecipeExpectation(
            "memory.ram_single_port",
            ["A0/1", "A,2,3D", "2EN3", "C2", "A"],
            ["3.3-13", "3.3-25", "4.3.7", "4.3.9", "4.3.11", "4.4.2", "5.14-1", "3.1-9"],
            ["WE", "Q"]);
    }

    public static DirectionExpectation GetDirectionExpectation(
        DirectionalOperation operation) => operation switch
        {
            DirectionalOperation.ShiftTowardHigh => new(
                "sequential.shift_register",
                "towardHigh",
                "SRG1",
                "C2/¬1,3→",
                "3.3-19",
                "3.3-20"),
            DirectionalOperation.ShiftTowardLow => new(
                "sequential.shift_register",
                "towardLow",
                "SRG1",
                "C2/¬1,3←",
                "3.3-20",
                "3.3-19"),
            DirectionalOperation.CountUp => new(
                "sequential.counter",
                "up",
                "CTR1",
                "C2/¬1,3+",
                "3.3-21",
                "3.3-22"),
            DirectionalOperation.CountDown => new(
                "sequential.counter",
                "down",
                "CTR1",
                "C2/¬1,3−",
                "3.3-22",
                "3.3-21"),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };
}

internal sealed record Item25RecipeExpectation(
    string ContractId,
    string[] DependencyLabels,
    string[] ClauseIds,
    string[] HiddenContractLabels);

internal enum DirectionalOperation
{
    ShiftTowardHigh,
    ShiftTowardLow,
    CountUp,
    CountDown,
}

internal sealed record DirectionExpectation(
    string ContractId,
    string Direction,
    string Function,
    string ClockLabel,
    string ClauseId,
    string ExcludedClauseId);
