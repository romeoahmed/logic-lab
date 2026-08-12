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
        if (!IsStableToken(policyId))
        {
            throw new ArgumentException(
                "The Package Policy ID must be a Stable Token.",
                nameof(policyId));
        }

        if (!IsStableToken(policyRevision))
        {
            throw new ArgumentException(
                "The Package Policy revision must be a Stable Token.",
                nameof(policyRevision));
        }

        var dimensions = Enum.GetValues<PackageDimension>();
        if (limits.Count != dimensions.Length)
        {
            throw new ArgumentException(
                "A package policy must define every dimension exactly once.",
                nameof(limits));
        }

        var ownedLimits = limits.ToArray();
        for (var index = 0; index < dimensions.Length; index++)
        {
            if (ownedLimits[index] is not { } limit
                || limit.Dimension != dimensions[index]
                || limit.Maximum == 0)
            {
                throw new ArgumentException(
                    "Package policy limits must be positive and in canonical dimension order.",
                    nameof(limits));
            }
        }

        this.limits = ownedLimits;
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

    public ulong GetMaximum(PackageDimension dimension)
    {
        if (!Enum.IsDefined(dimension))
        {
            throw new ArgumentOutOfRangeException(nameof(dimension));
        }

        return limits[(int)dimension].Maximum;
    }

    private static bool IsStableToken(string value)
    {
        return value.Length <= 96
            && IsAsciiLetterOrDigit(value[0])
            && value.All(character =>
                IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-');
    }

    private static bool IsAsciiLetterOrDigit(char value)
    {
        return value is >= 'A' and <= 'Z'
            or >= 'a' and <= 'z'
            or >= '0' and <= '9';
    }
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

public sealed class ProjectPackageReadRequest
{
    public ProjectPackageReadRequest(
        Stream source,
        PackagePolicy packagePolicy)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(packagePolicy);
        if (!source.CanRead)
        {
            throw new ArgumentException(
                "The package source must be readable.",
                nameof(source));
        }

        Source = source;
        PackagePolicy = packagePolicy;
    }

    public Stream Source { get; }

    public PackagePolicy PackagePolicy { get; }
}

public sealed record PackagePolicyIdentity(string PolicyId, string PolicyRevision);

public sealed record PackageDimensionObservation
{
    public PackageDimensionObservation(
        PackageDimension dimension,
        ulong observed)
    {
        DimensionToken = dimension switch
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

        Dimension = dimension;
        Observed = observed;
    }

    public PackageDimension Dimension { get; }

    public ulong Observed { get; }

    public string DimensionToken { get; }
}

public sealed record PackageEvidence(
    PackagePolicyIdentity Policy,
    IReadOnlyList<PackageDimensionObservation> ObservedDimensions,
    PackageDimensionObservation? PolicyLimitBreach);

public enum PackageDiagnosticSeverity
{
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

public abstract record PackageReadOutcome
{
    private protected PackageReadOutcome()
    {
    }
}

public sealed record PackageReadSucceeded(
    ProjectImportCandidate ImportCandidate,
    string ProjectContentDigest,
    string PackageDigest,
    PackageEvidence Evidence) : PackageReadOutcome;

public sealed record PackageReadRejected(
    string Reason,
    IReadOnlyList<PackageDiagnostic> Diagnostics,
    PackageEvidence Evidence) : PackageReadOutcome;
