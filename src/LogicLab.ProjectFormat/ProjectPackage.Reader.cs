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
        var declaredEntryCount = await ZipCentralDirectory.ReadDeclaredEntryCountAsync(
            spool,
            cancellationToken).ConfigureAwait(false);
        if (declaredEntryCount > policy.Maximum(PackageDimension.EntryCount))
        {
            observations[(int)PackageDimension.EntryCount] = declaredEntryCount;
            ThrowIfReadLimitExceeded(
                policy,
                observations,
                PackageDimension.EntryCount);
        }

        spool.Position = 0;
        await using var archive = await ZipArchive.CreateAsync(
            spool,
            ZipArchiveMode.Read,
            leaveOpen: true,
            entryNameEncoding: Encoding.UTF8,
            cancellationToken).ConfigureAwait(false);
        var entries = EnumerateEntries(archive, policy, observations);
        if (!entries.TryGetValue("manifest.json", out var manifestEntry)
            || !entries.ContainsKey("project.json"))
        {
            throw Invalid("package_illegal_entry", ("rule", "requiredPart"));
        }

        var manifestBytes = await ReadEntryAsync(
            manifestEntry,
            policy,
            observations,
            cancellationToken).ConfigureAwait(false);
        ValidateJson(manifestBytes, policy, observations);
        ValidateManifestMembers(manifestBytes);
        var manifest = JsonSerializer.Deserialize(
            manifestBytes,
            ReadJsonContext.PackageManifestDtoV1)
            ?? throw Invalid("package_json_invalid", ("rule", "schema"));
        ValidateManifest(manifest, entries);
        observations[(int)PackageDimension.MemoryPartCount] = checked(
            (ulong)manifest.MemoryParts.Length);
        ThrowIfReadLimitExceeded(
            policy,
            observations,
            PackageDimension.MemoryPartCount);

        var parts = new List<PackagePart>(checked(manifest.MemoryParts.Length + 1));
        var projectBytes = await ReadDeclaredPartAsync(
            entries,
            manifest.ProjectPart.Path,
            manifest.ProjectPart.Length,
            manifest.ProjectPart.Sha256,
            "project",
            memoryImageId: null,
            policy,
            observations,
            cancellationToken).ConfigureAwait(false);
        parts.Add(PackagePart.Create(
            manifest.ProjectPart.Path,
            projectBytes,
            memoryImageId: null,
            cancellationToken));

        var memoryBytes = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var memoryPart in manifest.MemoryParts.OrderBy(
                     item => item.MemoryImageId,
                     StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bytes = await ReadDeclaredPartAsync(
                entries,
                memoryPart.Path,
                memoryPart.Length,
                memoryPart.Sha256,
                "memory",
                memoryPart.MemoryImageId,
                policy,
                observations,
                cancellationToken).ConfigureAwait(false);
            memoryBytes.Add(memoryPart.MemoryImageId, bytes);
            parts.Add(PackagePart.Create(
                memoryPart.Path,
                bytes,
                memoryPart.MemoryImageId,
                cancellationToken));
        }

        var packageDigest = ComputeDigest(
            "logiclab-package-v1\0",
            parts,
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

        ValidateJson(projectBytes, policy, observations);
        ValidateProjectMembers(projectBytes);
        var decodedProject = JsonSerializer.Deserialize(
            projectBytes,
            ReadJsonContext.ProjectDocumentDtoV1)
            ?? throw Invalid("package_json_invalid", ("rule", "schema"));
        var project = MigrateProject(manifest.SchemaVersion, decodedProject);
        ObserveDecodedProject(project, policy, observations);
        var candidate = TranslateProject(
            project,
            manifest,
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
        normalizedParts.AddRange(parts
            .Where(part => part.MemoryImageId is not null)
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
        ulong[] observations)
    {
        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
        foreach (var entry in archive.Entries)
        {
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

    private static async Task<byte[]> ReadDeclaredPartAsync(
        Dictionary<string, ZipArchiveEntry> entries,
        string path,
        ulong declaredLength,
        string declaredHash,
        string partKind,
        string? memoryImageId,
        PackagePolicy policy,
        ulong[] observations,
        CancellationToken cancellationToken)
    {
        if (!entries.TryGetValue(path, out var entry))
        {
            throw Invalid("package_integrity_mismatch", ("partKind", partKind), ("check", "missing"));
        }

        var bytes = await ReadEntryAsync(
            entry,
            policy,
            observations,
            cancellationToken).ConfigureAwait(false);
        if (checked((ulong)bytes.Length) != declaredLength)
        {
            throw Invalid("package_integrity_mismatch", ("partKind", partKind), ("check", "length"));
        }

        var actualHash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (!string.Equals(declaredHash, actualHash, StringComparison.Ordinal))
        {
            throw Invalid("package_integrity_mismatch", ("partKind", partKind), ("check", "sha256"));
        }

        if (memoryImageId is not null
            && !string.Equals(path, $"memory/{memoryImageId}.bin", StringComparison.Ordinal))
        {
            throw Invalid("package_illegal_entry", ("rule", "memoryPath"));
        }

        return bytes;
    }

    private static async Task<byte[]> ReadEntryAsync(
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
            throw Invalid(
                "package_unsupported_feature",
                ("feature", "compression"));
        }
        catch (IOException)
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
        Dictionary<string, ZipArchiveEntry> entries)
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

        if (entries.Count != paths.Count
            || entries.Keys.Any(path => !paths.Contains(path)))
        {
            throw Invalid("package_illegal_entry", ("rule", "undeclaredPart"));
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
        ulong[] observations)
    {
        var entities = checked(1UL + (ulong)project.MemoryImages.Length);
        ulong memoryCells = 0;
        foreach (var image in project.MemoryImages)
        {
            var depth = ParseCanonicalUnsigned64(image.Depth, "depth");
            memoryCells = SaturatingAdd(
                memoryCells,
                SaturatingMultiply(image.WordWidth, depth));
        }

        foreach (var definition in project.CircuitDefinitions)
        {
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
        PackageManifestDtoV1 manifest,
        IReadOnlyDictionary<string, byte[]> memoryBytes,
        CancellationToken cancellationToken)
    {
        try
        {
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
            var manifestIds = manifest.MemoryParts
                .Select(item => item.MemoryImageId)
                .ToHashSet(StringComparer.Ordinal);
            EnsureDistinct(project.MemoryImages, item => item.Id, "memoryImage");
            var projectMemoryIds = project.MemoryImages
                .Select(item => item.Id)
                .ToHashSet(StringComparer.Ordinal);
            if (!manifestIds.SetEquals(projectMemoryIds))
            {
                throw Invalid(
                    "package_integrity_mismatch",
                    ("partKind", "memory"),
                    ("check", "agreement"));
            }

            var memoryImages = project.MemoryImages
                .OrderBy(item => item.Id, StringComparer.Ordinal)
                .Select(item => TranslateMemoryImage(
                    item,
                    manifestIds,
                    memoryBytes,
                    cancellationToken))
                .ToArray();
            EnsureDistinct(project.CircuitDefinitions, item => item.Id, "circuitDefinition");
            var definitions = project.CircuitDefinitions
                .OrderBy(item => item.Id, StringComparer.Ordinal)
                .Select(TranslateDefinition)
                .ToArray();
            var document = new ProjectDocument(
                new ProjectId(project.ProjectId),
                project.DisplayName,
                LibrarySnapshot.Core,
                symbolProfile,
                new CircuitDefinitionId(project.EntryCircuitDefinitionId),
                definitions,
                memoryImages);
            return new ProjectImportCandidate(document);
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
            payload);
    }

    private static CircuitDefinition TranslateDefinition(CircuitDefinitionDtoV1 definition)
    {
        RequireOpaqueId(definition.Id);
        var definitionId = new CircuitDefinitionId(definition.Id);
        EnsureDistinct(definition.Ports, item => item.Id, "definitionPort");
        EnsureDistinct(
            definition.ComponentInstances,
            item => item.Id,
            "componentInstance");
        EnsureDistinct(definition.Nets, item => item.Id, "net");
        EnsureDistinct(definition.Junctions, item => item.Id, "junction");
        EnsureDistinct(definition.WireGeometry, item => item.Id, "wireGeometry");
        EnsureDistinct(
            definition.Presentation.Annotations,
            item => item.Id,
            "annotation");
        var portPlacements = UniqueBy(
            definition.Presentation.DefinitionPortPlacements,
            item => item.PortId,
            "definitionPortPlacement");
        var componentPlacements = UniqueBy(
            definition.Presentation.ComponentPlacements,
            item => item.ComponentInstanceId,
            "componentPlacement");
        var ports = definition.Ports.Select(port =>
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
        }).ToArray();
        if (portPlacements.Count != ports.Length)
        {
            throw Invalid("package_domain_invalid", ("rule", "portPlacement"));
        }

        var components = definition.ComponentInstances
            .OrderBy(instance => instance.Id, StringComparer.Ordinal)
            .Select(instance =>
        {
            RequireOpaqueId(instance.Id);
            if (!componentPlacements.TryGetValue(instance.Id, out var placement))
            {
                throw Invalid("package_domain_invalid", ("rule", "componentPlacement"));
            }

            return new ComponentInstance(
                new ComponentInstanceId(instance.Id),
                TranslateTarget(instance.Target),
                [.. instance.Parameters.Select(TranslateParameter)],
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
        }).ToArray();
        if (componentPlacements.Count != components.Length)
        {
            throw Invalid("package_domain_invalid", ("rule", "componentPlacement"));
        }

        var nets = definition.Nets
            .OrderBy(net => net.Id, StringComparer.Ordinal)
            .Select(net => new Net(
            new NetId(RequireOpaqueId(net.Id)),
            net.Width,
            [.. net.Terminals.Select(terminal =>
                TranslateTerminal(definitionId, terminal))],
            [.. net.JunctionIds.Select(id => new JunctionId(RequireOpaqueId(id)))]))
            .ToArray();
        var junctions = definition.Junctions
            .OrderBy(junction => junction.Id, StringComparer.Ordinal)
            .Select(junction => new Junction(
            new JunctionId(RequireOpaqueId(junction.Id)),
            new NetId(RequireOpaqueId(junction.NetId)),
            ToPoint(junction.Position))).ToArray();
        var geometries = definition.WireGeometry
            .OrderBy(geometry => geometry.Id, StringComparer.Ordinal)
            .Select(geometry => new WireGeometry(
            new WireGeometryId(RequireOpaqueId(geometry.Id)),
            new NetId(RequireOpaqueId(geometry.NetId)),
            TranslateRoute(geometry.Route))).ToArray();
        var annotations = definition.Presentation.Annotations.Select(annotation =>
            new Annotation(
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
                    }))).ToArray();
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

    private static ComponentParameterBinding TranslateParameter(
        ParameterBindingDtoV1 binding)
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
                    new LogicVectorParameterValue(ParseLogicVector(value.Bits)),
                Unsigned32ListParameterDtoV1 value =>
                    new WidthsParameterValue(value.Values),
                SliceListParameterDtoV1 value => new SlicesParameterValue(
                    [.. value.Values.Select(slice => new BitSlice(
                        slice.Offset,
                        slice.Length))]),
                MemoryImageParameterDtoV1 value => new MemoryImageParameterValue(
                    new MemoryImageId(RequireOpaqueId(value.MemoryImageId))),
                _ => throw Invalid("package_unknown_discriminator"),
            });
    }

    private static LogicValue[] ParseLogicVector(string bits)
    {
        if (bits.Length == 0)
        {
            throw Invalid("package_json_invalid", ("rule", "logicVector"));
        }

        var values = new LogicValue[bits.Length];
        for (var index = 0; index < bits.Length; index++)
        {
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

    private static WireRoute TranslateRoute(WireRouteDtoV1 route) => route switch
    {
        UnroutedWireRouteDtoV1 => new UnroutedWireRoute(),
        OrthogonalWireRouteDtoV1 orthogonal => new OrthogonalWireRoute(
            [.. orthogonal.Points.Select(ToPoint)]),
        _ => throw Invalid("package_unknown_discriminator"),
    };

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
        string entityKind)
    {
        var result = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var value in values)
        {
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
        string entityKind)
    {
        _ = UniqueBy(values, selectId, entityKind);
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
