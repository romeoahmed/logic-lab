using System.Buffers;
using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using LogicLab.Domain;
using LogicLab.Domain.Authoring;

namespace LogicLab.ProjectFormat;

public static class ProjectPackage
{
    private static readonly DateTimeOffset CanonicalEntryTimestamp =
        new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static async Task<PackageWriteOutcome> WriteAsync(
        ProjectPackageWriteRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var observations = new ulong[Enum.GetValues<PackageDimension>().Length];

        if (cancellationToken.IsCancellationRequested)
        {
            return Rejected(
                request.PackagePolicy,
                "package_cancelled",
                [],
                observations,
                null);
        }

        try
        {
            ObserveDomain(request.ProjectRevision.Document, observations);
            var domainBreach = FindBreach(
                request.PackagePolicy,
                observations,
                includeCarrier: false);
            if (domainBreach is not null)
            {
                return LimitRejected(
                    request.PackagePolicy,
                    observations,
                    domainBreach);
            }

            var projectBytes = CanonicalProjectJson.Write(
                request.ProjectRevision.Document);
            var parts = new List<PackagePart>
            {
                PackagePart.Create("project.json", projectBytes),
            };

            foreach (var image in request.ProjectRevision.Document.MemoryImages.OrderBy(
                         item => item.Id.Value,
                         StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                parts.Add(PackagePart.Create(
                    $"memory/{image.Id.Value}.bin",
                    WriteMemoryImage(image),
                    image.Id.Value));
            }

            var packageDigest = ComputeDigest("logiclab-package-v1\0", parts);
            var projectContentDigest = ComputeDigest(
                "logiclab-project-content-v1\0",
                parts);
            var manifestBytes = WriteManifest(parts, packageDigest);
            Observe(
                request.ProjectRevision.Document,
                manifestBytes,
                parts,
                observations);

            var preflightBreach = FindBreach(
                request.PackagePolicy,
                observations,
                includeCarrier: false);
            if (preflightBreach is not null)
            {
                return LimitRejected(
                    request.PackagePolicy,
                    observations,
                    preflightBreach);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var carrierBytes = await WriteCarrierAsync(
                request.Destination,
                manifestBytes,
                parts,
                cancellationToken).ConfigureAwait(false);
            observations[(int)PackageDimension.CarrierBytes] = carrierBytes;

            var carrierBreach = FindBreach(
                request.PackagePolicy,
                observations,
                includeCarrier: true);
            if (carrierBreach is not null)
            {
                return LimitRejected(
                    request.PackagePolicy,
                    observations,
                    carrierBreach);
            }

            return new PackageWriteSucceeded(
                request.ProjectRevision.RevisionId,
                projectContentDigest,
                packageDigest,
                carrierBytes,
                Evidence(request.PackagePolicy, observations, null));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Rejected(
                request.PackagePolicy,
                "package_cancelled",
                [],
                observations,
                null);
        }
        catch (Exception exception) when (IsInfrastructureFailure(exception))
        {
            return Rejected(
                request.PackagePolicy,
                "package_infrastructure_failure",
                [],
                observations,
                null);
        }
        catch (Exception exception) when (!IsFatal(exception))
        {
            return Rejected(
                request.PackagePolicy,
                "package_internal_defect",
                [],
                observations,
                null);
        }
    }

    private static byte[] WriteMemoryImage(MemoryImage image)
    {
        var cellCount = checked((ulong)image.Width * image.Depth);
        var payloadLength = checked((cellCount + 3) / 4);
        var totalLength = checked(20UL + payloadLength);
        if (totalLength > int.MaxValue)
        {
            throw new OverflowException("A memory part cannot be represented in memory.");
        }

        var bytes = new byte[(int)totalLength];
        "LLMI"u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4, 2), 1);
        bytes[6] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8, 4), image.Width);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(12, 8), image.Depth);

        ulong cellIndex = 0;
        foreach (var word in image.Words)
        {
            foreach (var value in word.Values)
            {
                var encoded = value switch
                {
                    LogicValue.Zero => 0,
                    LogicValue.One => 1,
                    LogicValue.X => 2,
                    _ => throw new InvalidOperationException(
                        "An authored memory image cannot contain high impedance."),
                };
                var byteIndex = checked(20 + (int)(cellIndex / 4));
                var shift = checked((int)((cellIndex % 4) * 2));
                bytes[byteIndex] |= checked((byte)(encoded << shift));
                cellIndex++;
            }
        }

        if (cellIndex != cellCount)
        {
            throw new InvalidOperationException(
                "The authored memory image shape is inconsistent.");
        }

        return bytes;
    }

    private static byte[] WriteManifest(
        IReadOnlyList<PackagePart> parts,
        string packageDigest)
    {
        var projectPart = parts.Single(part => part.Path == "project.json");
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(
            buffer,
            new JsonWriterOptions
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                Indented = false,
                SkipValidation = false,
            }))
        {
            writer.WriteStartObject();
            writer.WriteString("format", "logiclab");
            writer.WriteNumber("schemaVersion", 1);
            writer.WritePropertyName("projectPart");
            WriteManifestPart(writer, projectPart);
            writer.WritePropertyName("memoryParts");
            writer.WriteStartArray();
            foreach (var part in parts
                         .Where(part => part.MemoryImageId is not null)
                         .OrderBy(part => part.MemoryImageId, StringComparer.Ordinal)
                         .ThenBy(part => part.Path, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("memoryImageId", part.MemoryImageId);
                writer.WriteString("path", part.Path);
                writer.WriteNumber("length", checked((ulong)part.Bytes.Length));
                writer.WriteString("sha256", part.HashHex);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteString("packageDigest", packageDigest);
            writer.WriteEndObject();
        }

        var bytes = new byte[buffer.WrittenCount + 1];
        buffer.WrittenSpan.CopyTo(bytes);
        bytes[^1] = (byte)'\n';
        return bytes;
    }

    private static void WriteManifestPart(Utf8JsonWriter writer, PackagePart part)
    {
        writer.WriteStartObject();
        writer.WriteString("path", part.Path);
        writer.WriteNumber("length", checked((ulong)part.Bytes.Length));
        writer.WriteString("sha256", part.HashHex);
        writer.WriteEndObject();
    }

    private static string ComputeDigest(
        string prefix,
        IReadOnlyList<PackagePart> parts)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(prefix));
        Span<byte> length = stackalloc byte[sizeof(ulong)];
        foreach (var part in parts.OrderBy(part => part.Path, StringComparer.Ordinal))
        {
            var pathBytes = Encoding.UTF8.GetBytes(part.Path);
            BinaryPrimitives.WriteUInt32LittleEndian(
                length[..sizeof(uint)],
                checked((uint)pathBytes.Length));
            hash.AppendData(length[..sizeof(uint)]);
            hash.AppendData(pathBytes);
            BinaryPrimitives.WriteUInt64LittleEndian(
                length,
                checked((ulong)part.Bytes.Length));
            hash.AppendData(length);
            hash.AppendData(part.Hash);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static async Task<ulong> WriteCarrierAsync(
        Stream destination,
        byte[] manifestBytes,
        IReadOnlyList<PackagePart> parts,
        CancellationToken cancellationToken)
    {
        var counting = new CountingWriteStream(destination);
        await using (var archive = await ZipArchive.CreateAsync(
                         counting,
                         ZipArchiveMode.Create,
                         leaveOpen: true,
                         entryNameEncoding: Encoding.UTF8,
                         cancellationToken).ConfigureAwait(false))
        {
            await WriteEntryAsync(
                archive,
                "manifest.json",
                manifestBytes,
                cancellationToken).ConfigureAwait(false);
            foreach (var part in parts)
            {
                await WriteEntryAsync(
                    archive,
                    part.Path,
                    part.Bytes,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        await counting.FlushAsync(cancellationToken).ConfigureAwait(false);
        return counting.BytesWritten;
    }

    private static async Task WriteEntryAsync(
        ZipArchive archive,
        string path,
        byte[] bytes,
        CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        entry.LastWriteTime = CanonicalEntryTimestamp;
        entry.ExternalAttributes = 0;
        await using var stream = await entry.OpenAsync(cancellationToken)
            .ConfigureAwait(false);
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
    }

    private static void Observe(
        ProjectDocument document,
        byte[] manifestBytes,
        IReadOnlyList<PackagePart> parts,
        ulong[] observations)
    {
        ObserveDomain(document, observations);
        observations[(int)PackageDimension.EntryCount] = checked((ulong)parts.Count + 1);
        observations[(int)PackageDimension.PartBytes] = Max(
            checked((ulong)manifestBytes.Length),
            parts.Select(part => checked((ulong)part.Bytes.Length)));
        observations[(int)PackageDimension.ExpandedBytes] = SaturatingAdd(
            checked((ulong)manifestBytes.Length),
            parts.Select(part => checked((ulong)part.Bytes.Length)));
        ObserveJson(manifestBytes, observations);
        foreach (var part in parts.Where(part => part.Path.EndsWith(".json", StringComparison.Ordinal)))
        {
            ObserveJson(part.Bytes, observations);
        }
    }

    private static void ObserveDomain(
        ProjectDocument document,
        ulong[] observations)
    {
        var memoryPartBytes = document.MemoryImages.Select(image =>
        {
            var cells = checked((ulong)image.Width * image.Depth);
            return SaturatingAdd(20, SaturatingAdd(cells, 3) / 4);
        }).ToArray();
        observations[(int)PackageDimension.EntryCount] =
            checked((ulong)document.MemoryImages.Count + 2);
        observations[(int)PackageDimension.PartBytes] = memoryPartBytes
            .DefaultIfEmpty(0UL)
            .Max();
        observations[(int)PackageDimension.ExpandedBytes] =
            SaturatingAdd(0, memoryPartBytes);
        observations[(int)PackageDimension.EntityCount] = ObserveEntities(document);
        observations[(int)PackageDimension.MemoryPartCount] =
            checked((ulong)document.MemoryImages.Count);
        observations[(int)PackageDimension.MemoryCellCount] =
            SaturatingAdd(
                0,
                document.MemoryImages.Select(
                    image => checked((ulong)image.Width * image.Depth)));
    }

    private static ulong ObserveEntities(ProjectDocument document)
    {
        var count = 1UL;
        count = SaturatingAdd(count, checked((ulong)document.CircuitDefinitions.Count));
        count = SaturatingAdd(count, checked((ulong)document.MemoryImages.Count));
        foreach (var definition in document.CircuitDefinitions)
        {
            count = SaturatingAdd(count, checked((ulong)definition.Ports.Count));
            count = SaturatingAdd(count, checked((ulong)definition.ComponentInstances.Count));
            count = SaturatingAdd(count, checked((ulong)definition.Nets.Count));
            count = SaturatingAdd(count, checked((ulong)definition.Junctions.Count));
            count = SaturatingAdd(count, checked((ulong)definition.WireGeometries.Count));
            count = SaturatingAdd(count, checked((ulong)definition.Annotations.Count));
        }

        return count;
    }

    private static void ObserveJson(byte[] bytes, ulong[] observations)
    {
        var reader = new Utf8JsonReader(bytes);
        var arrays = new List<bool>();
        while (reader.Read())
        {
            observations[(int)PackageDimension.JsonTokens] = SaturatingAdd(
                observations[(int)PackageDimension.JsonTokens],
                1);
            observations[(int)PackageDimension.JsonDepth] = Math.Max(
                observations[(int)PackageDimension.JsonDepth],
                checked((ulong)reader.CurrentDepth + 1));

            if (arrays.Count > 0
                && arrays[^1]
                && reader.TokenType is not (JsonTokenType.EndArray or JsonTokenType.EndObject))
            {
                observations[(int)PackageDimension.ArrayItems] = SaturatingAdd(
                    observations[(int)PackageDimension.ArrayItems],
                    1);
            }

            if (reader.TokenType is JsonTokenType.PropertyName or JsonTokenType.String)
            {
                var value = reader.GetString() ?? string.Empty;
                observations[(int)PackageDimension.StringScalarCount] = SaturatingAdd(
                    observations[(int)PackageDimension.StringScalarCount],
                    checked((ulong)value.EnumerateRunes().Count()));
                observations[(int)PackageDimension.StringUtf8Bytes] = SaturatingAdd(
                    observations[(int)PackageDimension.StringUtf8Bytes],
                    checked((ulong)Encoding.UTF8.GetByteCount(value)));
            }

            switch (reader.TokenType)
            {
                case JsonTokenType.StartArray:
                    arrays.Add(true);
                    break;
                case JsonTokenType.StartObject:
                    arrays.Add(false);
                    break;
                case JsonTokenType.EndArray:
                case JsonTokenType.EndObject:
                    arrays.RemoveAt(arrays.Count - 1);
                    break;
            }
        }
    }

    private static PackageDimensionObservation? FindBreach(
        PackagePolicy policy,
        ulong[] observations,
        bool includeCarrier)
    {
        foreach (var dimension in Enum.GetValues<PackageDimension>())
        {
            if (!includeCarrier && dimension == PackageDimension.CarrierBytes)
            {
                continue;
            }

            var observed = observations[(int)dimension];
            if (observed > policy.Maximum(dimension))
            {
                return new PackageDimensionObservation(dimension, observed);
            }
        }

        return null;
    }

    private static PackageWriteRejected LimitRejected(
        PackagePolicy policy,
        ulong[] observations,
        PackageDimensionObservation breach)
    {
        var diagnostic = new PackageDiagnostic(
            "package_limit_exceeded",
            PackageDiagnosticSeverity.Error,
            [
                new("policyId", policy.PolicyId),
                new("policyRevision", policy.PolicyRevision),
                new("dimension", PackageDimensionNames.Token(breach.Dimension)),
                new("observed", breach.Observed.ToString(CultureInfo.InvariantCulture)),
            ]);
        return Rejected(
            policy,
            "package_limit_exceeded",
            [diagnostic],
            observations,
            breach);
    }

    private static PackageWriteRejected Rejected(
        PackagePolicy policy,
        string reason,
        IReadOnlyList<PackageDiagnostic> diagnostics,
        ulong[] observations,
        PackageDimensionObservation? breach) =>
        new(reason, diagnostics, Evidence(policy, observations, breach));

    private static PackageEvidence Evidence(
        PackagePolicy policy,
        ulong[] observations,
        PackageDimensionObservation? breach) =>
        new(
            new PackagePolicyIdentity(policy.PolicyId, policy.PolicyRevision),
            Array.AsReadOnly(Enum.GetValues<PackageDimension>()
                .Select(dimension => new PackageDimensionObservation(
                    dimension,
                    observations[(int)dimension]))
                .ToArray()),
            breach);

    private static bool IsInfrastructureFailure(Exception exception) =>
        exception is IOException
            or UnauthorizedAccessException
            or ObjectDisposedException
            or NotSupportedException;

    private static bool IsFatal(Exception exception) => exception is
        OutOfMemoryException or StackOverflowException or AccessViolationException;

    private static ulong Max(ulong first, IEnumerable<ulong> rest)
    {
        var maximum = first;
        foreach (var value in rest)
        {
            maximum = Math.Max(maximum, value);
        }

        return maximum;
    }

    private static ulong SaturatingAdd(ulong left, ulong right) =>
        ulong.MaxValue - left < right ? ulong.MaxValue : left + right;

    private static ulong SaturatingAdd(ulong first, IEnumerable<ulong> rest)
    {
        var sum = first;
        foreach (var value in rest)
        {
            sum = SaturatingAdd(sum, value);
        }

        return sum;
    }

    private sealed record PackagePart(
        string Path,
        byte[] Bytes,
        byte[] Hash,
        string HashHex,
        string? MemoryImageId)
    {
        public static PackagePart Create(
            string path,
            byte[] bytes,
            string? memoryImageId = null)
        {
            var hash = SHA256.HashData(bytes);
            return new PackagePart(
                path,
                bytes,
                hash,
                Convert.ToHexStringLower(hash),
                memoryImageId);
        }
    }

    private sealed class CountingWriteStream(Stream destination) : Stream
    {
        private readonly ArrayBufferWriter<byte> deferredSynchronousWrites = new();

        public ulong BytesWritten { get; private set; }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => destination.CanWrite;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override async Task FlushAsync(CancellationToken cancellationToken)
        {
            await FlushDeferredWritesAsync(cancellationToken).ConfigureAwait(false);
            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
            deferredSynchronousWrites.Write(buffer.AsSpan(offset, count));
            BytesWritten = SaturatingAdd(BytesWritten, checked((ulong)count));
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            deferredSynchronousWrites.Write(buffer);
            BytesWritten = SaturatingAdd(
                BytesWritten,
                checked((ulong)buffer.Length));
        }

        public override async ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            await FlushDeferredWritesAsync(cancellationToken).ConfigureAwait(false);
            await destination.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
            BytesWritten = SaturatingAdd(
                BytesWritten,
                checked((ulong)buffer.Length));
        }

        public override async Task WriteAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            await FlushDeferredWritesAsync(cancellationToken).ConfigureAwait(false);
            await destination.WriteAsync(
                    buffer.AsMemory(offset, count),
                    cancellationToken)
                .ConfigureAwait(false);
            BytesWritten = SaturatingAdd(
                BytesWritten,
                checked((ulong)count));
        }

        private async Task FlushDeferredWritesAsync(
            CancellationToken cancellationToken)
        {
            if (deferredSynchronousWrites.WrittenCount == 0)
            {
                return;
            }

            await destination.WriteAsync(
                    deferredSynchronousWrites.WrittenMemory,
                    cancellationToken)
                .ConfigureAwait(false);
            deferredSynchronousWrites.Clear();
        }
    }
}
