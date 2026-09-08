using System.Collections.ObjectModel;
using LogicLab.Application.Examples;

namespace LogicLab.Web.Components.Editor;

internal static class StarterCircuitCatalog
{
    public static ReadOnlyCollection<ExampleProjectPlan> Examples { get; } =
        Array.AsReadOnly<ExampleProjectPlan>(
        [
            new(
                ExampleProject.Inverter,
                "author",
                "logic.not",
                "StarterInverterTitle",
                "StarterInverterDescription"),
            new(
                ExampleProject.Steering,
                "author-steering",
                "logic.mux",
                "StarterSteeringTitle",
                "StarterSteeringDescription"),
            new(
                ExampleProject.CarryLookahead,
                "author-carry-lookahead",
                "logic.adder",
                "StarterCarryLookaheadTitle",
                "StarterCarryLookaheadDescription"),
            new(
                ExampleProject.BitSerial,
                "author-bit-serial",
                "sequential.shift_register",
                "StarterBitSerialTitle",
                "StarterBitSerialDescription"),
        ]);

    public static ExampleProjectPlan GetPlan(ExampleProject example) =>
        Examples.FirstOrDefault(candidate => candidate.Example == example)
        ?? throw new ArgumentOutOfRangeException(nameof(example), example, null);

}

internal sealed record ExampleProjectPlan(
    ExampleProject Example,
    string Command,
    string SymbolContractId,
    string TitleResourceKey,
    string DescriptionResourceKey);
