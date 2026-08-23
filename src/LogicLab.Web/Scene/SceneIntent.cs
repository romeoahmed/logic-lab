namespace LogicLab.Web.Scene;

public sealed record SceneIntentV1(
    string BuildFingerprint,
    string Kind,
    ulong SceneVersion,
    ulong ProjectionVersion,
    string CircuitDefinitionId,
    IReadOnlyList<SceneSourceRefV1> Sources,
    string SelectionMode);

public sealed record SceneSelectionV1(
    IReadOnlyList<SceneSourceRefV1> Sources,
    string SelectionMode);
