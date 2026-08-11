using System.Collections.ObjectModel;
using LogicLab.Domain.Authoring;

namespace LogicLab.ProjectFormat;

public enum PackageDimension
{
    CarrierBytes,
    EntryCount,
    PartBytes,
    ExpandedBytes,
    JsonDepth,
    JsonTokens,
    StringScalarCount,
    StringUtf8Bytes,
    ArrayItems,
    EntityCount,
    MemoryPartCount,
    MemoryCellCount,
}

public sealed record PackageLimit(PackageDimension Dimension, ulong Maximum);

public sealed class PackagePolicy
{
    private readonly PackageLimit[] limits;

    public PackagePolicy(
        string policyId,
        string policyRevision,
        IReadOnlyList<PackageLimit> limits)
    {
        ArgumentException.ThrowIfNullOrEmpty(policyId);
        ArgumentException.ThrowIfNullOrEmpty(policyRevision);
        ArgumentNullException.ThrowIfNull(limits);

        var dimensions = Enum.GetValues<PackageDimension>();
        if (limits.Count != dimensions.Length)
        {
            throw new ArgumentException(
                "A package policy must define every dimension exactly once.",
                nameof(limits));
        }

        this.limits = [.. limits];
        for (var index = 0; index < dimensions.Length; index++)
        {
            var limit = this.limits[index];
            if (limit.Dimension != dimensions[index] || limit.Maximum == 0)
            {
                throw new ArgumentException(
                    "Package policy limits must be positive and in canonical dimension order.",
                    nameof(limits));
            }
        }

        PolicyId = policyId;
        PolicyRevision = policyRevision;
        Limits = Array.AsReadOnly(this.limits);
    }

    public static PackagePolicy Development { get; } = new(
        "logiclab-development-package",
        "1",
        [
            new(PackageDimension.CarrierBytes, 64UL * 1024 * 1024),
            new(PackageDimension.EntryCount, 1_026),
            new(PackageDimension.PartBytes, 64UL * 1024 * 1024),
            new(PackageDimension.ExpandedBytes, 128UL * 1024 * 1024),
            new(PackageDimension.JsonDepth, 64),
            new(PackageDimension.JsonTokens, 1_000_000),
            new(PackageDimension.StringScalarCount, 4_000_000),
            new(PackageDimension.StringUtf8Bytes, 16UL * 1024 * 1024),
            new(PackageDimension.ArrayItems, 1_000_000),
            new(PackageDimension.EntityCount, 250_000),
            new(PackageDimension.MemoryPartCount, 1_024),
            new(PackageDimension.MemoryCellCount, 64UL * 1024 * 1024),
        ]);

    public string PolicyId { get; }

    public string PolicyRevision { get; }

    public ReadOnlyCollection<PackageLimit> Limits { get; }

    internal ulong Maximum(PackageDimension dimension) =>
        limits[(int)dimension].Maximum;
}

public sealed class ProjectPackageWriteRequest
{
    public ProjectPackageWriteRequest(
        ProjectRevision projectRevision,
        Stream destination,
        PackagePolicy packagePolicy)
    {
        ArgumentNullException.ThrowIfNull(projectRevision);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(packagePolicy);
        if (!destination.CanWrite)
        {
            throw new ArgumentException(
                "The package destination must be writable.",
                nameof(destination));
        }

        ProjectRevision = projectRevision;
        Destination = destination;
        PackagePolicy = packagePolicy;
    }

    public ProjectRevision ProjectRevision { get; }

    public Stream Destination { get; }

    public PackagePolicy PackagePolicy { get; }
}

public sealed record PackagePolicyIdentity(string PolicyId, string PolicyRevision);

public sealed record PackageDimensionObservation(
    PackageDimension Dimension,
    ulong Observed);

public sealed record PackageEvidence(
    PackagePolicyIdentity Policy,
    IReadOnlyList<PackageDimensionObservation> ObservedDimensions,
    PackageDimensionObservation? PolicyLimitBreach);

public enum PackageDiagnosticSeverity
{
    Info,
    Warning,
    Error,
}

public sealed record PackageDiagnosticArgument(string Name, string Value);

public sealed record PackageDiagnostic(
    string Code,
    PackageDiagnosticSeverity Severity,
    IReadOnlyList<PackageDiagnosticArgument> Arguments);

public abstract record PackageWriteOutcome
{
    private protected PackageWriteOutcome()
    {
    }
}

public sealed record PackageWriteSucceeded(
    ProjectRevisionId SourceProjectRevisionId,
    string ProjectContentDigest,
    string PackageDigest,
    ulong CarrierByteCount,
    PackageEvidence Evidence) : PackageWriteOutcome;

public sealed record PackageWriteRejected(
    string Reason,
    IReadOnlyList<PackageDiagnostic> Diagnostics,
    PackageEvidence Evidence) : PackageWriteOutcome;

internal static class PackageDimensionNames
{
    public static string Token(PackageDimension dimension) => dimension switch
    {
        PackageDimension.CarrierBytes => "carrier_bytes",
        PackageDimension.EntryCount => "entry_count",
        PackageDimension.PartBytes => "part_bytes",
        PackageDimension.ExpandedBytes => "expanded_bytes",
        PackageDimension.JsonDepth => "json_depth",
        PackageDimension.JsonTokens => "json_tokens",
        PackageDimension.StringScalarCount => "string_scalar_count",
        PackageDimension.StringUtf8Bytes => "string_utf8_bytes",
        PackageDimension.ArrayItems => "array_items",
        PackageDimension.EntityCount => "entity_count",
        PackageDimension.MemoryPartCount => "memory_part_count",
        PackageDimension.MemoryCellCount => "memory_cell_count",
        _ => throw new ArgumentOutOfRangeException(nameof(dimension)),
    };
}
