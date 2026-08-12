using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;

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
            await using var spool = CreateImportSpool();
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

    private static FileStream CreateImportSpool()
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
            $"logiclab-import-{Guid.NewGuid():N}.tmp");
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
        if (centralDirectory.EntryCount > policy.Maximum(PackageDimension.EntryCount))
        {
            observations[(int)PackageDimension.EntryCount] = centralDirectory.EntryCount;
            ThrowIfReadLimitExceeded(
                policy,
                observations,
                PackageDimension.EntryCount);
        }

        var entryProfiles = await ZipCentralDirectory.ReadEntryProfilesAsync(
            spool,
            centralDirectory,
            cancellationToken).ConfigureAwait(false);
        if (entryProfiles.Any(profile =>
                profile.CentralCompressionMethod is not 0 and not 8
                || profile.LocalCompressionMethod is not 0 and not 8))
        {
            throw Invalid(
                "package_unsupported_feature",
                ("feature", "compression"));
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
            entryProfiles,
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
        await ValidateManifestMembersAsync(manifestBytes, cancellationToken)
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

        var partDigests = new List<PackagePartDigest>(
            checked(manifest.MemoryParts.Length + 1));
        var projectPart = await ReadDeclaredPartDigestAsync(
            entries,
            manifest.ProjectPart.Path,
            memoryImageId: null,
            policy,
            observations,
            cancellationToken).ConfigureAwait(false);
        partDigests.Add(projectPart);
        foreach (var memoryPart in manifest.MemoryParts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            partDigests.Add(await ReadDeclaredPartDigestAsync(
                entries,
                memoryPart.Path,
                memoryPart.MemoryImageId,
                policy,
                observations,
                cancellationToken).ConfigureAwait(false));
        }

        ValidatePartIntegrity(
            projectPart,
            manifest.ProjectPart.Length,
            manifest.ProjectPart.Sha256,
            "project");

        var projectBytes = await ReadValidatedPartBytesAsync(
            entries[manifest.ProjectPart.Path],
            manifest.ProjectPart.Length,
            cancellationToken).ConfigureAwait(false);

        ValidateJson(projectBytes, policy, observations, cancellationToken);
        await ValidateProjectMembersAsync(projectBytes, cancellationToken)
            .ConfigureAwait(false);
        using var projectStream = new MemoryStream(projectBytes, writable: false);
        var decodedProject = await JsonSerializer.DeserializeAsync(
            projectStream,
            ReadJsonContext.ProjectDocumentDtoV1,
            cancellationToken).ConfigureAwait(false)
            ?? throw Invalid("package_json_invalid", ("rule", "schema"));
        cancellationToken.ThrowIfCancellationRequested();
        var project = MigrateProject(manifest.SchemaVersion, decodedProject);
        ObserveDecodedProject(
            project,
            policy,
            observations,
            cancellationToken);
        var manifestMemoryIds = ValidateMemoryPartAgreement(
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

        var memoryBytes = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var memoryParts = new List<PackagePart>(manifest.MemoryParts.Length);
        foreach (var memoryPart in manifest.MemoryParts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bytes = await ReadValidatedPartBytesAsync(
                entries[memoryPart.Path],
                memoryPart.Length,
                cancellationToken).ConfigureAwait(false);
            memoryBytes.Add(memoryPart.MemoryImageId, bytes);
            memoryParts.Add(PackagePart.Create(
                memoryPart.Path,
                bytes,
                memoryPart.MemoryImageId,
                cancellationToken));
        }

        var candidate = TranslateProject(
            project,
            manifestMemoryIds,
            memoryBytes,
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
        normalizedParts.AddRange(memoryParts
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
        ZipEntryProfile[] entryProfiles,
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

            if (index >= entryProfiles.Length
                || entryProfiles[index].CentralCompressionMethod is not 0 and not 8
                || entryProfiles[index].LocalCompressionMethod is not 0 and not 8)
            {
                throw Invalid(
                    "package_unsupported_feature",
                    ("feature", "compression"));
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

    private static async Task<PackagePartDigest> ReadDeclaredPartDigestAsync(
        Dictionary<string, ZipArchiveEntry> entries,
        string path,
        string? memoryImageId,
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

        var part = await ReadEntryDigestAsync(
            entry,
            policy,
            observations,
            cancellationToken).ConfigureAwait(false);
        if (memoryImageId is not null
            && !string.Equals(path, $"memory/{memoryImageId}.bin", StringComparison.Ordinal))
        {
            throw Invalid("package_illegal_entry", ("rule", "memoryPath"));
        }

        return new PackagePartDigest(
            path,
            part.Length,
            part.Hash,
            part.HashHex,
            memoryImageId);
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

        if (!string.Equals(declaredHash, part.HashHex, StringComparison.Ordinal))
        {
            throw Invalid(
                "package_integrity_mismatch",
                ("partKind", partKind),
                ("check", "sha256"));
        }
    }

    private static async Task<ReadPartDigest> ReadEntryDigestAsync(
        ZipArchiveEntry entry,
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
            }

            var hash = hashing.GetHashAndReset();
            return new ReadPartDigest(
                partBytes,
                hash,
                Convert.ToHexStringLower(hash));
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
        try
        {
            await using var source = await entry.OpenAsync(cancellationToken)
                .ConfigureAwait(false);
            using var destination = new MemoryStream();
            var buffer = new byte[ReadBufferSize];
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
                destination.Write(buffer, 0, read);
            }

            return destination.ToArray();
        }
        catch (InvalidDataException)
        {
            throw Invalid("package_illegal_entry", ("rule", "corruptPart"));
        }
        catch (NotSupportedException)
        {
            throw Invalid(
                "package_unsupported_feature",
                ("feature", "compression"));
        }
    }

    private static async Task<byte[]> ReadValidatedPartBytesAsync(
        ZipArchiveEntry entry,
        ulong validatedLength,
        CancellationToken cancellationToken)
    {
        if (validatedLength > int.MaxValue)
        {
            throw Invalid("package_illegal_entry", ("rule", "partLength"));
        }

        try
        {
            var bytes = GC.AllocateUninitializedArray<byte>(checked((int)validatedLength));
            await using var source = await entry.OpenAsync(cancellationToken)
                .ConfigureAwait(false);
            await source.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
            var extra = new byte[1];
            if (await source.ReadAsync(extra, cancellationToken)
                    .ConfigureAwait(false) != 0)
            {
                throw Invalid("package_integrity_mismatch", ("partKind", "part"), ("check", "length"));
            }

            return bytes;
        }
        catch (InvalidDataException)
        {
            throw Invalid("package_illegal_entry", ("rule", "corruptPart"));
        }
        catch (NotSupportedException)
        {
            throw Invalid(
                "package_unsupported_feature",
                ("feature", "compression"));
        }
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

    private static ProjectDocumentDtoV1 MigrateProject(
        ulong schemaVersion,
        ProjectDocumentDtoV1 project)
    {
        return schemaVersion switch
        {
            1 => project,
            _ => throw Invalid(
                "package_schema_version_unsupported",
                ("actual", schemaVersion.ToString(CultureInfo.InvariantCulture))),
        };
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

    private static ProjectImportCandidate TranslateProject(
        ProjectDocumentDtoV1 project,
        HashSet<string> manifestMemoryIds,
        IReadOnlyDictionary<string, byte[]> memoryBytes,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequireOpaqueId(project.ProjectId);
            RequireOpaqueId(project.EntryCircuitDefinitionId);
            if (project.LibraryReferences.Length != 1)
            {
                throw Invalid("package_domain_invalid", ("rule", "libraryReference"));
            }

            var library = project.LibraryReferences[0];
            if (!string.Equals(library.Id, LibrarySnapshot.Core.LibraryId, StringComparison.Ordinal)
                || !string.Equals(library.Version, LibrarySnapshot.Core.Version, StringComparison.Ordinal)
                || !string.Equals(library.Digest, LibrarySnapshot.Core.ContentDigest, StringComparison.Ordinal))
            {
                throw Invalid("package_domain_invalid", ("rule", "librarySnapshot"));
            }

            var symbolProfile = new SymbolProfileReference(
                RequireStableName(project.SymbolProfile.Id),
                RequireStableVersion(project.SymbolProfile.Version),
                project.SymbolProfile.IndicationConvention switch
                {
                    "negation" => IndicationConvention.Negation,
                    "directPolarity" => IndicationConvention.DirectPolarity,
                    _ => throw Invalid("package_json_invalid", ("rule", "indicationConvention")),
                });
            var memoryImages = Map(
                OrderedById(
                    project.MemoryImages,
                    item => item.Id,
                    cancellationToken),
                item => TranslateMemoryImage(
                    item,
                    manifestMemoryIds,
                    memoryBytes,
                    cancellationToken),
                cancellationToken);

            EnsureDistinct(
                project.CircuitDefinitions,
                item => item.Id,
                "circuitDefinition",
                cancellationToken);
            var definitions = Map(
                OrderedById(
                    project.CircuitDefinitions,
                    item => item.Id,
                    cancellationToken),
                item => TranslateDefinition(item, cancellationToken),
                cancellationToken);
            var document = new ProjectDocument(
                new ProjectId(project.ProjectId),
                project.DisplayName,
                LibrarySnapshot.Core,
                symbolProfile,
                new CircuitDefinitionId(project.EntryCircuitDefinitionId),
                definitions,
                memoryImages);
            return new ProjectImportCandidate(document, cancellationToken);
        }
        catch (PackageReadInvalidException)
        {
            throw;
        }
        catch (Exception exception) when (exception is
            ArgumentException or InvalidOperationException or OverflowException)
        {
            throw Invalid("package_domain_invalid", ("rule", "authoringInvariant"));
        }
    }

    private static MemoryImage TranslateMemoryImage(
        MemoryImageRefDtoV1 image,
        HashSet<string> manifestIds,
        IReadOnlyDictionary<string, byte[]> memoryBytes,
        CancellationToken cancellationToken)
    {
        RequireOpaqueId(image.Id);
        var expectedPath = $"memory/{image.Id}.bin";
        if (!string.Equals(image.PartPath, expectedPath, StringComparison.Ordinal)
            || !manifestIds.Contains(image.Id)
            || !memoryBytes.TryGetValue(image.Id, out var bytes))
        {
            throw Invalid("package_integrity_mismatch", ("partKind", "memory"), ("check", "agreement"));
        }

        var depth = ParseCanonicalUnsigned64(image.Depth, "depth");
        if (depth is 0 or > uint.MaxValue || image.WordWidth == 0)
        {
            throw Invalid("package_memory_invalid", ("rule", "shape"));
        }

        return DecodeMemoryImage(
            image,
            checked((uint)depth),
            bytes,
            cancellationToken);
    }

    private static HashSet<string> ValidateMemoryPartAgreement(
        ProjectDocumentDtoV1 project,
        PackageManifestDtoV1 manifest,
        CancellationToken cancellationToken)
    {
        EnsureDistinct(
            project.MemoryImages,
            item => item.Id,
            "memoryImage",
            cancellationToken);
        var projectMemoryIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var image in project.MemoryImages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = RequireOpaqueId(image.Id);
            if (!string.Equals(
                    image.PartPath,
                    $"memory/{id}.bin",
                    StringComparison.Ordinal))
            {
                throw Invalid(
                    "package_integrity_mismatch",
                    ("partKind", "memory"),
                    ("check", "agreement"));
            }

            projectMemoryIds.Add(id);
        }

        var manifestMemoryIds = ToIdSet(
            manifest.MemoryParts,
            item => item.MemoryImageId,
            cancellationToken);
        if (!manifestMemoryIds.SetEquals(projectMemoryIds))
        {
            throw Invalid(
                "package_integrity_mismatch",
                ("partKind", "memory"),
                ("check", "agreement"));
        }

        return manifestMemoryIds;
    }

    private static MemoryImage DecodeMemoryImage(
        MemoryImageRefDtoV1 reference,
        uint depth,
        byte[] bytes,
        CancellationToken cancellationToken)
    {
        if (bytes.Length < 20
            || !bytes.AsSpan(0, 4).SequenceEqual("LLMI"u8)
            || BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(4, 2)) != 1
            || bytes[6] != 1
            || bytes[7] != 0)
        {
            throw Invalid("package_memory_invalid", ("rule", "header"));
        }

        var width = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8, 4));
        var encodedDepth = BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(12, 8));
        if (width != reference.WordWidth || encodedDepth != depth)
        {
            throw Invalid("package_memory_invalid", ("rule", "shapeAgreement"));
        }

        var cellCount = checked((ulong)width * depth);
        var payloadLength = checked((cellCount + 3) / 4);
        if (checked(20UL + payloadLength) != checked((ulong)bytes.Length))
        {
            throw Invalid("package_memory_invalid", ("rule", "payloadLength"));
        }

        var payload = bytes.AsSpan(20);
        for (var index = 0; index < payload.Length; index++)
        {
            if ((index & (CancellationInterval - 1)) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var value = payload[index];
            if (index == payload.Length - 1
                && cellCount % 4 is var payloadUsedFields and not 0)
            {
                var usedBits = checked((int)payloadUsedFields * 2);
                value &= checked((byte)((1 << usedBits) - 1));
            }

            if ((value & (value >> 1) & 0x55) != 0)
            {
                throw Invalid("package_memory_invalid", ("rule", "reservedCell"));
            }
        }

        var usedFields = checked((int)(cellCount % 4));
        if (usedFields != 0)
        {
            var usedBits = checked(usedFields * 2);
            var unusedMask = unchecked((byte)~((1 << usedBits) - 1));
            if ((bytes[^1] & unusedMask) != 0)
            {
                throw Invalid("package_memory_invalid", ("rule", "tailFields"));
            }
        }

        return new MemoryImage(
            new MemoryImageId(reference.Id),
            reference.DisplayName,
            width,
            depth,
            payload,
            cancellationToken);
    }

    private static CircuitDefinition TranslateDefinition(
        CircuitDefinitionDtoV1 definition,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequireOpaqueId(definition.Id);
        var definitionId = new CircuitDefinitionId(definition.Id);
        EnsureDistinct(
            definition.Ports,
            item => item.Id,
            "definitionPort",
            cancellationToken);
        EnsureDistinct(
            definition.ComponentInstances,
            item => item.Id,
            "componentInstance",
            cancellationToken);
        EnsureDistinct(
            definition.Nets,
            item => item.Id,
            "net",
            cancellationToken);
        EnsureDistinct(
            definition.Junctions,
            item => item.Id,
            "junction",
            cancellationToken);
        EnsureDistinct(
            definition.WireGeometry,
            item => item.Id,
            "wireGeometry",
            cancellationToken);
        EnsureDistinct(
            definition.Presentation.Annotations,
            item => item.Id,
            "annotation",
            cancellationToken);
        var portPlacements = UniqueBy(
            definition.Presentation.DefinitionPortPlacements,
            item => item.PortId,
            "definitionPortPlacement",
            cancellationToken);
        var componentPlacements = UniqueBy(
            definition.Presentation.ComponentPlacements,
            item => item.ComponentInstanceId,
            "componentPlacement",
            cancellationToken);
        var ports = Map(definition.Ports, port =>
        {
            RequireOpaqueId(port.Id);
            if (!portPlacements.TryGetValue(port.Id, out var placement))
            {
                throw Invalid("package_domain_invalid", ("rule", "portPlacement"));
            }

            return new DefinitionPort(
                new DefinitionPortId(port.Id),
                port.DisplayName,
                port.Direction switch
                {
                    "input" => PortDirection.Input,
                    "output" => PortDirection.Output,
                    _ => throw Invalid("package_json_invalid", ("rule", "portDirection")),
                },
                port.Width,
                new DefinitionPortPlacement(
                    ToPoint(placement.Position),
                    ToCardinalDirection(placement.Facing)));
        }, cancellationToken);
        if (portPlacements.Count != ports.Length)
        {
            throw Invalid("package_domain_invalid", ("rule", "portPlacement"));
        }

        var components = Map(
            OrderedById(
                definition.ComponentInstances,
                item => item.Id,
                cancellationToken),
            instance =>
        {
            RequireOpaqueId(instance.Id);
            if (!componentPlacements.TryGetValue(instance.Id, out var placement))
            {
                throw Invalid("package_domain_invalid", ("rule", "componentPlacement"));
            }

            return new ComponentInstance(
                new ComponentInstanceId(instance.Id),
                TranslateTarget(instance.Target),
                TranslateParameters(instance.Parameters, cancellationToken),
                new ComponentPlacement(
                    ToPoint(placement.Origin),
                    placement.Orientation.QuarterTurnsClockwise switch
                    {
                        0 => QuarterTurn.Zero,
                        1 => QuarterTurn.One,
                        2 => QuarterTurn.Two,
                        3 => QuarterTurn.Three,
                        _ => throw Invalid("package_json_invalid", ("rule", "orientation")),
                    },
                    placement.Orientation.Reflected),
                instance.DisplayName,
                placement.SymbolVariantId);
        }, cancellationToken);
        if (componentPlacements.Count != components.Length)
        {
            throw Invalid("package_domain_invalid", ("rule", "componentPlacement"));
        }

        var nets = Map(
            OrderedById(
                definition.Nets,
                item => item.Id,
                cancellationToken),
            net =>
        {
            var terminals = Map(
                net.Terminals,
                terminal => TranslateTerminal(
                    definitionId,
                    terminal),
                cancellationToken);
            var junctionIds = Map(
                net.JunctionIds,
                id => new JunctionId(RequireOpaqueId(id)),
                cancellationToken);
            return new Net(
                new NetId(RequireOpaqueId(net.Id)),
                net.Width,
                terminals,
                junctionIds);
        }, cancellationToken);

        var junctions = Map(
            OrderedById(
                definition.Junctions,
                item => item.Id,
                cancellationToken),
            junction => new Junction(
                new JunctionId(RequireOpaqueId(junction.Id)),
                new NetId(RequireOpaqueId(junction.NetId)),
                ToPoint(junction.Position)),
            cancellationToken);

        var geometries = Map(
            OrderedById(
                definition.WireGeometry,
                item => item.Id,
                cancellationToken),
            geometry => new WireGeometry(
                new WireGeometryId(RequireOpaqueId(geometry.Id)),
                new NetId(RequireOpaqueId(geometry.NetId)),
                TranslateRoute(geometry.Route, cancellationToken)),
            cancellationToken);

        var annotations = Map(
            definition.Presentation.Annotations,
            annotation => new Annotation(
                new AnnotationId(RequireOpaqueId(annotation.Id)),
                new AnnotationValue(
                    annotation.Text,
                    ToPoint(annotation.Position),
                    annotation.Alignment switch
                    {
                        "start" => AnnotationAlignment.Start,
                        "center" => AnnotationAlignment.Center,
                        "end" => AnnotationAlignment.End,
                        _ => throw Invalid("package_json_invalid", ("rule", "annotationAlignment")),
                    })),
            cancellationToken);
        return new CircuitDefinition(
            definitionId,
            definition.DisplayName,
            ports,
            components,
            nets,
            junctions,
            geometries,
            annotations);
    }

    private static ComponentTarget TranslateTarget(ComponentTargetDtoV1 target) =>
        target switch
        {
            LibraryContractTargetDtoV1 library => new LibraryComponentTarget(
                new ComponentContractKey(
                    RequireStableName(library.LibraryId),
                    RequireStableName(library.ContractId))),
            CircuitDefinitionTargetDtoV1 definition =>
                new CircuitDefinitionComponentTarget(
                    new CircuitDefinitionId(
                        RequireOpaqueId(definition.CircuitDefinitionId))),
            _ => throw Invalid("package_unknown_discriminator"),
        };

    private static ComponentParameterBinding[] TranslateParameters(
        ParameterBindingDtoV1[] bindings,
        CancellationToken cancellationToken) => Map(
            bindings,
            binding => TranslateParameter(binding, cancellationToken),
            cancellationToken);

    private static ComponentParameterBinding TranslateParameter(
        ParameterBindingDtoV1 binding,
        CancellationToken cancellationToken)
    {
        return new ComponentParameterBinding(
            RequireStableName(binding.ParameterId),
            binding.Value switch
            {
                Unsigned32ParameterDtoV1 value =>
                    new Unsigned32ParameterValue(value.Value),
                Unsigned64ParameterDtoV1 value =>
                    new Unsigned64ParameterValue(
                        ParseCanonicalUnsigned64(value.Decimal, "unsigned64")),
                EnumParameterDtoV1 value =>
                    new ChoiceParameterValue(RequireStableName(value.Value)),
                LogicVectorParameterDtoV1 value =>
                    new LogicVectorParameterValue(ParseLogicVector(
                        value.Bits,
                        cancellationToken)),
                Unsigned32ListParameterDtoV1 value =>
                    new WidthsParameterValue(value.Values),
                SliceListParameterDtoV1 value => new SlicesParameterValue(
                    TranslateSlices(value.Values, cancellationToken)),
                MemoryImageParameterDtoV1 value => new MemoryImageParameterValue(
                    new MemoryImageId(RequireOpaqueId(value.MemoryImageId))),
                _ => throw Invalid("package_unknown_discriminator"),
            });
    }

    private static BitSlice[] TranslateSlices(
        BitSliceDtoV1[] slices,
        CancellationToken cancellationToken) => Map(
            slices,
            slice => new BitSlice(slice.Offset, slice.Length),
            cancellationToken);

    private static LogicValue[] ParseLogicVector(
        string bits,
        CancellationToken cancellationToken)
    {
        if (bits.Length == 0)
        {
            throw Invalid("package_json_invalid", ("rule", "logicVector"));
        }

        var values = new LogicValue[bits.Length];
        for (var index = 0; index < bits.Length; index++)
        {
            if ((index & (CancellationInterval - 1)) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            values[bits.Length - 1 - index] = bits[index] switch
            {
                '0' => LogicValue.Zero,
                '1' => LogicValue.One,
                'X' => LogicValue.X,
                _ => throw Invalid("package_json_invalid", ("rule", "logicVector")),
            };
        }

        return values;
    }

    private static AuthoredTerminalReference TranslateTerminal(
        CircuitDefinitionId definitionId,
        TerminalReferenceDtoV1 terminal) => terminal switch
        {
            DefinitionPortTerminalDtoV1 port => new DefinitionTerminalReference(
                definitionId,
                new DefinitionPortId(RequireOpaqueId(port.PortId))),
            InstancePortTerminalDtoV1 instance => new InstanceTerminalReference(
                definitionId,
                new ComponentInstanceId(RequireOpaqueId(instance.ComponentInstanceId)),
                RequireStableName(instance.PortId)),
            _ => throw Invalid("package_unknown_discriminator"),
        };

    private static WireRoute TranslateRoute(
        WireRouteDtoV1 route,
        CancellationToken cancellationToken) => route switch
        {
            UnroutedWireRouteDtoV1 => new UnroutedWireRoute(),
            OrthogonalWireRouteDtoV1 orthogonal => new OrthogonalWireRoute(
                TranslatePoints(orthogonal.Points, cancellationToken)),
            _ => throw Invalid("package_unknown_discriminator"),
        };

    private static GridPoint[] TranslatePoints(
        GridPointDtoV1[] points,
        CancellationToken cancellationToken) => Map(
            points,
            ToPoint,
            cancellationToken);

    private static GridPoint ToPoint(GridPointDtoV1 point) => new(point.X, point.Y);

    private static CardinalDirection ToCardinalDirection(string value) => value switch
    {
        "north" => CardinalDirection.North,
        "east" => CardinalDirection.East,
        "south" => CardinalDirection.South,
        "west" => CardinalDirection.West,
        _ => throw Invalid("package_json_invalid", ("rule", "facing")),
    };

    private static Dictionary<string, T> UniqueBy<T>(
        IEnumerable<T> values,
        Func<T, string> selectId,
        string entityKind,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = RequireOpaqueId(selectId(value));
            if (!result.TryAdd(id, value))
            {
                throw Invalid("package_domain_invalid", ("rule", $"duplicate{entityKind}"));
            }
        }

        return result;
    }

    private static void EnsureDistinct<T>(
        IEnumerable<T> values,
        Func<T, string> selectId,
        string entityKind,
        CancellationToken cancellationToken)
    {
        _ = UniqueBy(
            values,
            selectId,
            entityKind,
            cancellationToken);
    }

    private static HashSet<string> ToIdSet<T>(
        IEnumerable<T> values,
        Func<T, string> selectId,
        CancellationToken cancellationToken)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ids.Add(selectId(value));
        }

        return ids;
    }

    private static T[] OrderedById<T>(
        IReadOnlyCollection<T> values,
        Func<T, string> selectId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = values.ToArray();
        cancellationToken.ThrowIfCancellationRequested();
        Array.Sort(
            result,
            (left, right) => string.CompareOrdinal(selectId(left), selectId(right)));
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    private static TOutput[] Map<TInput, TOutput>(
        IReadOnlyList<TInput> values,
        Func<TInput, TOutput> transform,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = new TOutput[values.Count];
        for (var index = 0; index < values.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            result[index] = transform(values[index]);
            cancellationToken.ThrowIfCancellationRequested();
        }

        return result;
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
            && IsAsciiLetter(value[0])
            && value.All(character =>
                IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-')
            ? value
            : throw Invalid("package_json_invalid", ("rule", "stableName"));
    }

    private static string RequireStableVersion(string value)
    {
        return value is { Length: >= 1 and <= 64 }
            && IsAsciiLetterOrDigit(value[0])
            && value.All(character =>
                IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-')
            ? value
            : throw Invalid("package_json_invalid", ("rule", "stableVersion"));
    }

    private static bool IsSha256(string? value) =>
        value is { Length: 64 }
        && value.All(character => character is >= '0' and <= '9'
            or >= 'a' and <= 'f');

    private static bool IsAsciiLowerOrDigit(char value) =>
        value is >= 'a' and <= 'z' or >= '0' and <= '9';

    private static bool IsAsciiLetterOrDigit(char value) =>
        IsAsciiLetter(value) || value is >= '0' and <= '9';

    private static bool IsAsciiLetter(char value) =>
        value is >= 'A' and <= 'Z' or >= 'a' and <= 'z';

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
        if (observed > policy.Maximum(dimension))
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
            ("dimension", breach.GetDimensionToken()),
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
