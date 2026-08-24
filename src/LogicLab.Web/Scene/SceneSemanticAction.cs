namespace LogicLab.Web.Scene;

public abstract record SceneSemanticActionV1(SceneSourceRefV1 Source);

public sealed record ActivateSceneSemanticActionV1(SceneSourceRefV1 Source)
    : SceneSemanticActionV1(Source);

public sealed record NudgeSceneSemanticActionV1 : SceneSemanticActionV1
{
    public NudgeSceneSemanticActionV1(SceneSourceRefV1 source, int deltaX, int deltaY)
        : base(source)
    {
        if (Math.Abs(deltaX) + Math.Abs(deltaY) != 1)
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
