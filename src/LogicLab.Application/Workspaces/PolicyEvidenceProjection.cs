namespace LogicLab.Application.Workspaces;

public sealed record PolicyEvidenceProjection
{
    public PolicyEvidenceProjection(
        string policyId,
        string policyRevision,
        string dimension,
        ulong observed)
    {
        ArgumentException.ThrowIfNullOrEmpty(policyId);
        ArgumentException.ThrowIfNullOrEmpty(policyRevision);
        ArgumentException.ThrowIfNullOrEmpty(dimension);
        PolicyId = policyId;
        PolicyRevision = policyRevision;
        Dimension = dimension;
        Observed = observed;
    }

    public string PolicyId { get; }

    public string PolicyRevision { get; }

    public string Dimension { get; }

    public ulong Observed { get; }
}
