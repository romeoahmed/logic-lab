namespace LogicLab.Application.Examples;

public enum ExampleProject
{
    Inverter,
    Steering,
    CarryLookahead,
    BitSerial,
}

internal static class ExampleProjects
{
    public static Stream Open(ExampleProject example)
    {
        if (!Enum.IsDefined(example))
        {
            throw new ArgumentOutOfRangeException(nameof(example));
        }

        return typeof(ExampleProjects).Assembly.GetManifestResourceStream(
            $"LogicLab.Application.Examples.Assets.{example}.logiclab")
            ?? throw new InvalidOperationException("The built-in example resource is missing.");
    }
}
