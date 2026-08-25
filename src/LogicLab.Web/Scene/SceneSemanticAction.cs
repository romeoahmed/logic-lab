namespace LogicLab.Web.Scene;

public abstract record SceneSemanticActionV1(SceneSourceRefV1 Source);

public sealed record ActivateSceneSemanticActionV1 : SceneSemanticActionV1
{
    public ActivateSceneSemanticActionV1(SceneSourceRefV1 source, string selectionMode)
        : base(source)
    {
        if (selectionMode is not ("replace" or "add" or "toggle"))
        {
            throw new ArgumentOutOfRangeException(nameof(selectionMode));
        }

        SelectionMode = selectionMode;
    }

    public string SelectionMode { get; }
}

public sealed record NudgeSceneSemanticActionV1 : SceneSemanticActionV1
{
    public NudgeSceneSemanticActionV1(SceneSourceRefV1 source, int deltaX, int deltaY)
        : base(source)
    {
        var isHorizontal = deltaX is -1 or 1 && deltaY == 0;
        var isVertical = deltaX == 0 && deltaY is -1 or 1;
        if (!isHorizontal && !isVertical)
        {
            throw new ArgumentException("A semantic nudge moves exactly one grid step.");
        }

        DeltaX = deltaX;
        DeltaY = deltaY;
    }

    public int DeltaX { get; }

    public int DeltaY { get; }
}

public sealed record RemoveSceneSemanticActionV1(SceneSourceRefV1 Source)
    : SceneSemanticActionV1(Source);
