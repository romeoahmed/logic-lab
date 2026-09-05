using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LogicLab.ProjectFormat;

public static partial class ProjectPackage
{
    private const int ReadBufferSize = 64 * 1024;
    private static readonly ProjectPackageJsonContext ReadJsonContext = new(
        new JsonSerializerOptions(JsonSerializerOptions.Strict)
        {
            AllowOutOfOrderMetadataProperties = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });

    public static async Task<PackageReadOutcome> ReadAsync(
        ProjectPackageReadRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var observations = new ulong[request.PackagePolicy.Limits.Count];
        if (cancellationToken.IsCancellationRequested)
        {
            return ReadRejected(
                request.PackagePolicy,
                "package_cancelled",
                [],
                observations,
                null);
        }

        try
        {
            await using var spool = CreateTemporaryFile("import");
            await SpoolAsync(
                request.Source,
                spool,
                request.PackagePolicy,
                observations,
                cancellationToken).ConfigureAwait(false);
            spool.Position = 0;
            return await ReadSpoolAsync(
                spool,
                request.PackagePolicy,
                observations,
                cancellationToken).ConfigureAwait(false);
        }
        catch (PackagePolicyLimitException exception)
        {
            return ReadLimitRejected(
                request.PackagePolicy,
                observations,
                exception.Breach);
        }
        catch (PackageReadInvalidException exception)
        {
            return ReadRejected(
                request.PackagePolicy,
                "package_invalid",
                [exception.Diagnostic],
                observations,
                null);
        }
        catch (JsonException)
        {
            return ReadRejected(
                request.PackagePolicy,
                "package_invalid",
                [Diagnostic("package_json_invalid", ("rule", "schema"))],
                observations,
                null);
        }
        catch (InvalidDataException)
        {
            return ReadRejected(
                request.PackagePolicy,
                "package_invalid",
                [Diagnostic("package_illegal_entry", ("rule", "carrier"))],
                observations,
                null);
        }
        catch (OperationCanceledException exception)
            when (IsCooperativeCancellation(exception, cancellationToken))
        {
            return ReadRejected(
                request.PackagePolicy,
                "package_cancelled",
                [],
                observations,
                null);
        }
        catch (IOException)
        {
            return ReadRejected(
                request.PackagePolicy,
                "package_infrastructure_failure",
                [],
                observations,
                null);
        }
        catch (UnauthorizedAccessException)
        {
            return ReadRejected(
                request.PackagePolicy,
                "package_infrastructure_failure",
                [],
                observations,
                null);
        }
        catch (Exception exception) when (!IsFatal(exception))
        {
            return ReadRejected(
                request.PackagePolicy,
                "package_internal_defect",
                [],
                observations,
                null);
        }
    }

    private static FileStream CreateTemporaryFile(string purpose)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.ReadWrite,
            Share = FileShare.None,
            BufferSize = ReadBufferSize,
            Options = FileOptions.Asynchronous
                | FileOptions.SequentialScan
                | FileOptions.DeleteOnClose,
        };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        var path = Path.Combine(
            Path.GetTempPath(),
            $"logiclab-{purpose}-{Guid.NewGuid():N}.tmp");
        return new FileStream(path, options);
    }

    private static async Task SpoolAsync(
        Stream source,
        FileStream spool,
        PackagePolicy policy,
        ulong[] observations,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[ReadBufferSize];
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            observations[(int)PackageDimension.CarrierBytes] = SaturatingAdd(
                observations[(int)PackageDimension.CarrierBytes],
                checked((ulong)read));
            ThrowIfReadLimitExceeded(
                policy,
                observations,
                PackageDimension.CarrierBytes);
            await spool.WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                .ConfigureAwait(false);
        }

        await spool.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<PackageReadOutcome> ReadSpoolAsync(
        FileStream spool,
        PackagePolicy policy,
        ulong[] observations,
        CancellationToken cancellationToken)
    {
        var centralDirectory = await ZipCentralDirectory.ReadInfoAsync(
            spool,
            cancellationToken).ConfigureAwait(false);
        if (centralDirectory.EntryCount > policy.GetMaximum(PackageDimension.EntryCount))
        {
            observations[(int)PackageDimension.EntryCount] = centralDirectory.EntryCount;
            ThrowIfReadLimitExceeded(
                policy,
                observations,
                PackageDimension.EntryCount);
        }

        var unsupportedFeature = await ZipCentralDirectory.FindUnsupportedFeatureAsync(
            spool,
            centralDirectory,
            cancellationToken).ConfigureAwait(false);
        if (unsupportedFeature is not null)
        {
            throw Invalid(
                "package_unsupported_feature",
                ("feature", unsupportedFeature == ZipUnsupportedFeature.Encryption
                    ? "encryption"
                    : "compression"));
        }

        spool.Position = 0;
        await using var archive = await ZipArchive.CreateAsync(
            spool,
            ZipArchiveMode.Read,
            leaveOpen: true,
            entryNameEncoding: Encoding.UTF8,
            cancellationToken).ConfigureAwait(false);
        var entries = EnumerateEntries(
            archive,
            policy,
            observations,
            cancellationToken);
        if (!entries.TryGetValue("manifest.json", out var manifestEntry)
            || !entries.ContainsKey("project.json"))
        {
            throw Invalid("package_illegal_entry", ("rule", "requiredPart"));
        }

        var manifestBytes = await ReadEntryBytesAsync(
            manifestEntry,
            policy,
            observations,
            cancellationToken).ConfigureAwait(false);
        ValidateJson(manifestBytes, policy, observations, cancellationToken);
        await ValidateMembersAsync(
                manifestBytes, ReadJsonContext.PackageManifestDtoV1, cancellationToken)
            .ConfigureAwait(false);
        using var manifestStream = new MemoryStream(manifestBytes, writable: false);
        var manifest = await JsonSerializer.DeserializeAsync(
            manifestStream,
            ReadJsonContext.PackageManifestDtoV1,
            cancellationToken).ConfigureAwait(false)
            ?? throw Invalid("package_json_invalid", ("rule", "schema"));
        cancellationToken.ThrowIfCancellationRequested();
        ValidateManifest(manifest, entries, cancellationToken);
        observations[(int)PackageDimension.MemoryPartCount] = checked(
            (ulong)manifest.MemoryParts.Length);
        ThrowIfReadLimitExceeded(
            policy,
            observations,
            PackageDimension.MemoryPartCount);

        await using var decodedParts = CreateTemporaryFile("parts");
        var spooledParts = new List<SpooledPackagePart>(
            checked(manifest.MemoryParts.Length + 1));
        var projectPart = await ReadDeclaredPartAsync(
            entries,
            manifest.ProjectPart.Path,
            memoryImageId: null,
            decodedParts,
            policy,
            observations,
            cancellationToken).ConfigureAwait(false);
        spooledParts.Add(projectPart);
        foreach (var memoryPart in manifest.MemoryParts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            spooledParts.Add(await ReadDeclaredPartAsync(
                entries,
                memoryPart.Path,
                memoryPart.MemoryImageId,
                decodedParts,
                policy,
                observations,
                cancellationToken).ConfigureAwait(false));
        }

        var partDigests = spooledParts.Select(part => part.Digest).ToArray();

        ValidatePartIntegrity(
            projectPart.Digest,
            manifest.ProjectPart.Length,
            manifest.ProjectPart.Sha256,
            "project");

        var projectBytes = await ReadSpooledPartBytesAsync(
            decodedParts,
            projectPart,
            cancellationToken).ConfigureAwait(false);

        ValidateJson(projectBytes, policy, observations, cancellationToken);
        await ValidateMembersAsync(
                projectBytes, ReadJsonContext.ProjectDocumentDtoV1, cancellationToken)
            .ConfigureAwait(false);
        using var projectStream = new MemoryStream(projectBytes, writable: false);
        var project = await JsonSerializer.DeserializeAsync(
            projectStream,
            ReadJsonContext.ProjectDocumentDtoV1,
            cancellationToken).ConfigureAwait(false)
            ?? throw Invalid("package_json_invalid", ("rule", "schema"));
        cancellationToken.ThrowIfCancellationRequested();
        ObserveDecodedProject(
            project,
            policy,
            observations,
            cancellationToken);
        ValidateMemoryPartAgreement(
            project,
            manifest,
            cancellationToken);
        for (var index = 0; index < manifest.MemoryParts.Length; index++)
        {
            var memoryPart = manifest.MemoryParts[index];
            ValidatePartIntegrity(
                partDigests[index + 1],
                memoryPart.Length,
                memoryPart.Sha256,
                "memory");
        }

        var packageDigest = ComputeDigest(
            "logiclab-package-v1\0",
            partDigests,
            cancellationToken);
        if (!string.Equals(
                manifest.PackageDigest,
                packageDigest,
                StringComparison.Ordinal))
        {
            throw Invalid(
                "package_integrity_mismatch",
                ("partKind", "package"),
                ("check", "digest"));
        }

        var memoryParts = new Dictionary<string, PackagePart>(StringComparer.Ordinal);
        for (var index = 0; index < manifest.MemoryParts.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var memoryPart = manifest.MemoryParts[index];
            var bytes = await ReadSpooledPartBytesAsync(
                decodedParts,
                spooledParts[index + 1],
                cancellationToken).ConfigureAwait(false);
            memoryParts.Add(memoryPart.MemoryImageId, new PackagePart(
                memoryPart.Path,
                bytes,
                partDigests[index + 1].Hash,
                memoryPart.MemoryImageId));
        }

        var candidate = TranslateProject(
            project,
            memoryParts,
            cancellationToken);
        var normalizedObservations = new ulong[policy.Limits.Count];
        var canonicalProjectByteCount = CanonicalProjectJson.Measure(
            candidate.Document,
            normalizedObservations,
            policy,
            cancellationToken);
        var normalizedParts = new List<PackagePart>
        {
            PackagePart.Create(
                "project.json",
                CanonicalProjectJson.Write(
                    candidate.Document,
                    canonicalProjectByteCount,
                    cancellationToken),
                memoryImageId: null,
                cancellationToken),
        };
        normalizedParts.AddRange(memoryParts.Values
            .OrderBy(part => part.Path, StringComparer.Ordinal));
        var projectContentDigest = ComputeDigest(
            "logiclab-project-content-v1\0",
            normalizedParts,
            cancellationToken);
        return new PackageReadSucceeded(
            candidate,
            projectContentDigest,
            packageDigest,
            Evidence(policy, observations, null));
    }

    private static Dictionary<string, ZipArchiveEntry> EnumerateEntries(
        ZipArchive archive,
        PackagePolicy policy,
        ulong[] observations,
        CancellationToken cancellationToken)
    {
        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
        for (var index = 0; index < archive.Entries.Count; index++)
        {
            var entry = archive.Entries[index];
            cancellationToken.ThrowIfCancellationRequested();
            observations[(int)PackageDimension.EntryCount] = SaturatingAdd(
                observations[(int)PackageDimension.EntryCount],
                1);
            ThrowIfReadLimitExceeded(
                policy,
                observations,
                PackageDimension.EntryCount);
            ValidateEntryPath(entry.FullName);
            if (entry.IsEncrypted)
            {
                throw Invalid(
                    "package_unsupported_feature",
                    ("feature", "encryption"));
            }

            if (!entries.TryAdd(entry.FullName, entry))
            {
                throw Invalid("package_duplicate_entry");
            }
        }

        return entries;
    }

    private static void ValidateEntryPath(string path)
    {
        if (path.Length == 0
            || path[0] == '/'
            || path[^1] == '/'
            || path.Any(character => character is < (char)0x21 or > (char)0x7e
                or '\\' or ':' or '\0')
            || path.Split('/').Any(segment => segment.Length == 0
                || segment is "." or ".."))
        {
            throw Invalid("package_illegal_entry", ("rule", "path"));
        }

        if (path is "manifest.json" or "project.json")
        {
            return;
        }

        if (!path.StartsWith("memory/", StringComparison.Ordinal)
            || !path.EndsWith(".bin", StringComparison.Ordinal)
            || !IsOpaqueId(path[7..^4]))
        {
            throw Invalid("package_illegal_entry", ("rule", "unknownPart"));
        }
    }

    private static async Task<SpooledPackagePart> ReadDeclaredPartAsync(
        Dictionary<string, ZipArchiveEntry> entries,
        string path,
        string? memoryImageId,
        FileStream destination,
        PackagePolicy policy,
        ulong[] observations,
        CancellationToken cancellationToken)
    {
        if (!entries.TryGetValue(path, out var entry))
        {
            throw Invalid(
                "package_integrity_mismatch",
                ("partKind", memoryImageId is null ? "project" : "memory"),
                ("check", "missing"));
        }

        var part = await ReadEntryAsync(
            entry,
            destination,
            policy,
            observations,
            cancellationToken).ConfigureAwait(false);
        if (memoryImageId is not null
            && !string.Equals(path, $"memory/{memoryImageId}.bin", StringComparison.Ordinal))
        {
            throw Invalid("package_illegal_entry", ("rule", "memoryPath"));
        }

        return part;
    }

    private static void ValidatePartIntegrity(
        PackagePartDigest part,
        ulong declaredLength,
        string declaredHash,
        string partKind)
    {
        if (part.Length != declaredLength)
        {
            throw Invalid(
                "package_integrity_mismatch",
                ("partKind", partKind),
                ("check", "length"));
        }

        if (!string.Equals(
                declaredHash,
                Convert.ToHexStringLower(part.Hash),
                StringComparison.Ordinal))
        {
            throw Invalid(
                "package_integrity_mismatch",
                ("partKind", partKind),
                ("check", "sha256"));
        }
    }

    private static async Task<SpooledPackagePart> ReadEntryAsync(
        ZipArchiveEntry entry,
        Stream destination,
        PackagePolicy policy,
        ulong[] observations,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var source = await entry.OpenAsync(cancellationToken)
                .ConfigureAwait(false);
            using var hashing = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[ReadBufferSize];
            var offset = destination.Position;
            ulong partBytes = 0;
            while (true)
            {
                var read = await source.ReadAsync(buffer, cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                partBytes = SaturatingAdd(partBytes, checked((ulong)read));
                observations[(int)PackageDimension.PartBytes] = Math.Max(
                    observations[(int)PackageDimension.PartBytes],
                    partBytes);
                observations[(int)PackageDimension.ExpandedBytes] = SaturatingAdd(
                    observations[(int)PackageDimension.ExpandedBytes],
                    checked((ulong)read));
                ThrowIfReadLimitExceeded(
                    policy,
                    observations,
                    PackageDimension.PartBytes);
                ThrowIfReadLimitExceeded(
                    policy,
                    observations,
                    PackageDimension.ExpandedBytes);
                hashing.AppendData(buffer, 0, read);
                await destination.WriteAsync(
                    buffer.AsMemory(0, read),
                    cancellationToken).ConfigureAwait(false);
            }

            var hash = hashing.GetHashAndReset();
            return new SpooledPackagePart(
                new PackagePartDigest(entry.FullName, partBytes, hash),
                offset);
        }
        catch (InvalidDataException)
        {
            throw Invalid(
                "package_illegal_entry",
                ("rule", "corruptPart"));
        }
        catch (NotSupportedException)
        {
            throw Invalid(
                "package_unsupported_feature",
                ("feature", "compression"));
        }
    }

    private static async Task<byte[]> ReadEntryBytesAsync(
        ZipArchiveEntry entry,
        PackagePolicy policy,
        ulong[] observations,
        CancellationToken cancellationToken)
    {
        using var destination = new MemoryStream();
        _ = await ReadEntryAsync(
            entry, destination, policy, observations, cancellationToken).ConfigureAwait(false);
        return destination.ToArray();
    }

    private static async Task<byte[]> ReadSpooledPartBytesAsync(
        FileStream source,
        SpooledPackagePart part,
        CancellationToken cancellationToken)
    {
        if (part.Digest.Length > int.MaxValue)
        {
            throw Invalid("package_illegal_entry", ("rule", "partLength"));
        }

        var bytes = GC.AllocateUninitializedArray<byte>(checked((int)part.Digest.Length));
        source.Position = part.Offset;
        await source.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
        return bytes;
    }

    private static void ValidateManifest(
        PackageManifestDtoV1 manifest,
        Dictionary<string, ZipArchiveEntry> entries,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(manifest.Format, "logiclab", StringComparison.Ordinal))
        {
            throw Invalid("package_json_invalid", ("rule", "format"));
        }

        if (manifest.SchemaVersion != 1)
        {
            throw Invalid(
                "package_schema_version_unsupported",
                ("actual", manifest.SchemaVersion.ToString(CultureInfo.InvariantCulture)));
        }

        if (!string.Equals(manifest.ProjectPart.Path, "project.json", StringComparison.Ordinal)
            || !IsSha256(manifest.ProjectPart.Sha256)
            || !IsSha256(manifest.PackageDigest))
        {
            throw Invalid("package_json_invalid", ("rule", "manifest"));
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var paths = new HashSet<string>(StringComparer.Ordinal)
        {
            "manifest.json",
            "project.json",
        };
        string? priorMemoryImageId = null;
        foreach (var part in manifest.MemoryParts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsOpaqueId(part.MemoryImageId)
                || !string.Equals(
                    part.Path,
                    $"memory/{part.MemoryImageId}.bin",
                    StringComparison.Ordinal)
                || !IsSha256(part.Sha256)
                || !ids.Add(part.MemoryImageId)
                || !paths.Add(part.Path))
            {
                throw Invalid("package_json_invalid", ("rule", "memoryPart"));
            }

            if (priorMemoryImageId is not null
                && string.CompareOrdinal(priorMemoryImageId, part.MemoryImageId) >= 0)
            {
                throw Invalid("package_json_invalid", ("rule", "memoryPartOrder"));
            }

            priorMemoryImageId = part.MemoryImageId;
        }

        if (entries.Count != paths.Count)
        {
            throw Invalid("package_illegal_entry", ("rule", "undeclaredPart"));
        }

        foreach (var path in entries.Keys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!paths.Contains(path))
            {
                throw Invalid("package_illegal_entry", ("rule", "undeclaredPart"));
            }
        }
    }

    private static void ObserveDecodedProject(
        ProjectDocumentDtoV1 project,
        PackagePolicy policy,
        ulong[] observations,
        CancellationToken cancellationToken)
    {
        var entities = checked(1UL + (ulong)project.MemoryImages.Length);
        ulong memoryCells = 0;
        foreach (var image in project.MemoryImages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var depth = ParseCanonicalUnsigned64(image.Depth, "depth");
            if (image.WordWidth == 0
                || image.WordWidth > int.MaxValue
                || depth is 0 or > int.MaxValue)
            {
                throw Invalid("package_memory_invalid", ("rule", "shape"));
            }

            var cellCount = SaturatingMultiply(image.WordWidth, depth);
            var payloadLength = SaturatingAdd(20, SaturatingAdd(cellCount, 3) / 4);
            if (payloadLength > int.MaxValue)
            {
                throw Invalid("package_memory_invalid", ("rule", "shape"));
            }

            memoryCells = SaturatingAdd(
                memoryCells,
                cellCount);
        }

        foreach (var definition in project.CircuitDefinitions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            entities = SaturatingAdd(entities, 1);
            entities = SaturatingAdd(entities, checked((ulong)definition.Ports.Length));
            entities = SaturatingAdd(
                entities,
                checked((ulong)definition.ComponentInstances.Length));
            entities = SaturatingAdd(entities, checked((ulong)definition.Nets.Length));
            entities = SaturatingAdd(entities, checked((ulong)definition.Junctions.Length));
            entities = SaturatingAdd(
                entities,
                checked((ulong)definition.WireGeometry.Length));
            entities = SaturatingAdd(
                entities,
                checked((ulong)definition.Presentation.Annotations.Length));
        }

        observations[(int)PackageDimension.EntityCount] = entities;
        observations[(int)PackageDimension.MemoryPartCount] = Math.Max(
            observations[(int)PackageDimension.MemoryPartCount],
            checked((ulong)project.MemoryImages.Length));
        observations[(int)PackageDimension.MemoryCellCount] = memoryCells;
        ThrowIfReadLimitExceeded(policy, observations, PackageDimension.EntityCount);
        ThrowIfReadLimitExceeded(policy, observations, PackageDimension.MemoryPartCount);
        ThrowIfReadLimitExceeded(policy, observations, PackageDimension.MemoryCellCount);
    }

    private static string RequireOpaqueId(string value)
    {
        return IsOpaqueId(value)
            ? value
            : throw Invalid("package_json_invalid", ("rule", "opaqueId"));
    }

    private static bool IsOpaqueId(string? value)
    {
        return value is { Length: >= 1 and <= 64 }
            && IsAsciiLowerOrDigit(value[0])
            && value.All(character =>
                IsAsciiLowerOrDigit(character) || character is '_' or '-');
    }

    private static string RequireStableName(string value)
    {
        return value is { Length: >= 1 and <= 96 }
            && char.IsAsciiLetter(value[0])
            && value.All(character =>
                char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-')
            ? value
            : throw Invalid("package_json_invalid", ("rule", "stableName"));
    }

    private static string RequireStableVersion(string value)
    {
        return value is { Length: >= 1 and <= 64 }
            && char.IsAsciiLetterOrDigit(value[0])
            && value.All(character =>
                char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-')
            ? value
            : throw Invalid("package_json_invalid", ("rule", "stableVersion"));
    }

    private static bool IsSha256(string? value) =>
        value is { Length: 64 }
        && value.All(char.IsAsciiHexDigitLower);

    private static bool IsAsciiLowerOrDigit(char value) =>
        char.IsAsciiLetterLower(value) || char.IsAsciiDigit(value);

    private static ulong ParseCanonicalUnsigned64(string value, string rule)
    {
        if (!ulong.TryParse(
                value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var result)
            || !string.Equals(
                value,
                result.ToString(CultureInfo.InvariantCulture),
                StringComparison.Ordinal))
        {
            throw Invalid("package_json_invalid", ("rule", rule));
        }

        return result;
    }

    private static ulong SaturatingMultiply(ulong left, ulong right) =>
        left == 0 || right <= ulong.MaxValue / left
            ? left * right
            : ulong.MaxValue;

    private static void ThrowIfReadLimitExceeded(
        PackagePolicy policy,
        ulong[] observations,
        PackageDimension dimension)
    {
        var observed = observations[(int)dimension];
        if (observed > policy.GetMaximum(dimension))
        {
            throw new PackagePolicyLimitException(
                new PackageDimensionObservation(dimension, observed));
        }
    }

    private static PackageReadRejected ReadLimitRejected(
        PackagePolicy policy,
        ulong[] observations,
        PackageDimensionObservation breach)
    {
        observations[(int)breach.Dimension] = Math.Max(
            observations[(int)breach.Dimension],
            breach.Observed);
        var diagnostic = Diagnostic(
            "package_limit_exceeded",
            ("policyId", policy.PolicyId),
            ("policyRevision", policy.PolicyRevision),
            ("dimension", breach.DimensionToken),
            ("observed", breach.Observed.ToString(CultureInfo.InvariantCulture)));
        return ReadRejected(
            policy,
            "package_limit_exceeded",
            [diagnostic],
            observations,
            breach);
    }

    private static PackageReadRejected ReadRejected(
        PackagePolicy policy,
        string reason,
        IReadOnlyList<PackageDiagnostic> diagnostics,
        ulong[] observations,
        PackageDimensionObservation? breach)
    {
        return new PackageReadRejected(
            reason,
            diagnostics,
            Evidence(policy, observations, breach));
    }

    private static PackageDiagnostic Diagnostic(
        string code,
        params (string Name, string Value)[] arguments)
    {
        return new PackageDiagnostic(
            code,
            PackageDiagnosticSeverity.Error,
            [.. arguments.Select(argument =>
                new PackageDiagnosticArgument(argument.Name, argument.Value))]);
    }

    private static PackageReadInvalidException Invalid(
        string code,
        params (string Name, string Value)[] arguments) =>
        new(Diagnostic(code, arguments));

    private sealed class PackageReadInvalidException(
        PackageDiagnostic diagnostic) : Exception
    {
        public PackageDiagnostic Diagnostic { get; } = diagnostic;
    }
}
