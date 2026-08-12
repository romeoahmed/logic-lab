using System.Buffers;
using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LogicLab.Domain;
using LogicLab.Domain.Authoring;

namespace LogicLab.ProjectFormat;

public static partial class ProjectPackage
{
    private const int CancellationInterval = 4_096;
    private static readonly DateTimeOffset CanonicalEntryTimestamp =
        new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static async Task<PackageWriteOutcome> WriteAsync(
        ProjectPackageWriteRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var observations = new ulong[request.PackagePolicy.Limits.Count];

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
            ObserveDomain(
                request.ProjectRevision.Document,
                observations,
                cancellationToken);
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

            var projectByteCount = CanonicalProjectJson.Measure(
                request.ProjectRevision.Document,
                observations,
                request.PackagePolicy,
                cancellationToken);
            ObserveProjectPart(projectByteCount, observations);
            var projectBreach = FindBreach(
                request.PackagePolicy,
                observations,
                includeCarrier: false);
            if (projectBreach is not null)
            {
                return LimitRejected(
                    request.PackagePolicy,
                    observations,
                    projectBreach);
            }

            var projectBytes = CanonicalProjectJson.Write(
                request.ProjectRevision.Document,
                projectByteCount,
                cancellationToken);
            var parts = new List<PackagePart>
            {
                PackagePart.Create(
                    "project.json",
                    projectBytes,
                    memoryImageId: null,
                    cancellationToken),
            };

            foreach (var image in request.ProjectRevision.Document.MemoryImages.OrderBy(
                         item => item.Id.Value,
                         StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                parts.Add(PackagePart.Create(
                    $"memory/{image.Id.Value}.bin",
                    WriteMemoryImage(image, cancellationToken),
                    image.Id.Value,
                    cancellationToken));
            }

            var packageDigest = ComputeDigest(
                "logiclab-package-v1\0",
                parts,
                cancellationToken);
            var projectContentDigest = ComputeDigest(
                "logiclab-project-content-v1\0",
                parts,
                cancellationToken);
            var manifestByteCount = MeasureManifest(
                parts,
                packageDigest,
                observations,
                request.PackagePolicy,
                cancellationToken);
            ObservePackage(
                manifestByteCount,
                parts,
                observations,
                cancellationToken);

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

            var manifestBytes = WriteManifest(
                parts,
                packageDigest,
                manifestByteCount,
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            var countingDestination = new CountingWriteStream(request.Destination);
            try
            {
                await WriteCarrierAsync(
                    countingDestination,
                    manifestBytes,
                    parts,
                    cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                observations[(int)PackageDimension.CarrierBytes] =
                    countingDestination.BytesWritten;
            }

            var carrierBytes = countingDestination.BytesWritten;

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
        catch (OperationCanceledException exception)
            when (IsCooperativeCancellation(exception, cancellationToken))
        {
            return Rejected(
                request.PackagePolicy,
                "package_cancelled",
                [],
                observations,
                null);
        }
        catch (PackagePolicyLimitException exception)
        {
            return LimitRejected(
                request.PackagePolicy,
                observations,
                exception.Breach);
        }
        catch (DestinationWriteException)
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

    private static byte[] WriteMemoryImage(
        MemoryImage image,
        CancellationToken cancellationToken)
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

        for (var index = 0; index < image.PackedCells.Length; index++)
        {
            if ((index & (CancellationInterval - 1)) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            bytes[20 + index] = image.PackedCells[index];
        }

        return bytes;
    }

    private static ulong MeasureManifest(
        IReadOnlyList<PackagePart> parts,
        string packageDigest,
        ulong[] observations,
        PackagePolicy policy,
        CancellationToken cancellationToken)
    {
        return CanonicalJson.Measure(
            writer => WriteManifestDocument(writer, parts, packageDigest),
            observations,
            policy,
            cancellationToken);
    }

    private static byte[] WriteManifest(
        IReadOnlyList<PackagePart> parts,
        string packageDigest,
        ulong measuredByteCount,
        CancellationToken cancellationToken)
    {
        return CanonicalJson.Write(
            writer => WriteManifestDocument(writer, parts, packageDigest),
            measuredByteCount,
            cancellationToken);
    }

    private static void WriteManifestDocument(
        CanonicalJsonWriter writer,
        IReadOnlyList<PackagePart> parts,
        string packageDigest)
    {
        var projectPart = parts[0];
        writer.WriteStartObject();
        writer.WriteString("format", "logiclab");
        writer.WriteNumber("schemaVersion", 1);
        writer.WritePropertyName("projectPart");
        WriteManifestPart(writer, projectPart);
        writer.WritePropertyName("memoryParts");
        writer.WriteStartArray();
        for (var index = 1; index < parts.Count; index++)
        {
            writer.CancellationToken.ThrowIfCancellationRequested();
            var part = parts[index];
            writer.WriteStartObject();
            writer.WriteString(
                "memoryImageId",
                part.MemoryImageId
                    ?? throw new InvalidOperationException(
                        "A memory package part must identify its Memory Image."));
            writer.WriteString("path", part.Path);
            writer.WriteNumber("length", checked((ulong)part.Bytes.Length));
            writer.WriteString("sha256", part.HashHex);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteString("packageDigest", packageDigest);
        writer.WriteEndObject();
    }

    private static void WriteManifestPart(
        CanonicalJsonWriter writer,
        PackagePart part)
    {
        writer.WriteStartObject();
        writer.WriteString("path", part.Path);
        writer.WriteNumber("length", checked((ulong)part.Bytes.Length));
        writer.WriteString("sha256", part.HashHex);
        writer.WriteEndObject();
    }

    private static string ComputeDigest(
        string prefix,
        IReadOnlyList<PackagePart> parts,
        CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(prefix));
        Span<byte> length = stackalloc byte[sizeof(ulong)];
        foreach (var part in parts.OrderBy(part => part.Path, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
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

    private static async Task WriteCarrierAsync(
        CountingWriteStream destination,
        byte[] manifestBytes,
        IReadOnlyList<PackagePart> parts,
        CancellationToken cancellationToken)
    {
        await using (var archive = await ZipArchive.CreateAsync(
                         destination,
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

        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
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

    private static void ObservePackage(
        ulong manifestByteCount,
        IReadOnlyList<PackagePart> parts,
        ulong[] observations,
        CancellationToken cancellationToken)
    {
        var largestPartBytes = manifestByteCount;
        var expandedBytes = largestPartBytes;
        foreach (var part in parts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var partBytes = checked((ulong)part.Bytes.Length);
            largestPartBytes = Math.Max(largestPartBytes, partBytes);
            expandedBytes = SaturatingAdd(expandedBytes, partBytes);
        }

        observations[(int)PackageDimension.EntryCount] = checked((ulong)parts.Count + 1);
        observations[(int)PackageDimension.PartBytes] = largestPartBytes;
        observations[(int)PackageDimension.ExpandedBytes] = expandedBytes;
    }

    private static void ObserveProjectPart(
        ulong projectByteCount,
        ulong[] observations)
    {
        observations[(int)PackageDimension.PartBytes] = Math.Max(
            observations[(int)PackageDimension.PartBytes],
            projectByteCount);
        observations[(int)PackageDimension.ExpandedBytes] = SaturatingAdd(
            observations[(int)PackageDimension.ExpandedBytes],
            projectByteCount);
    }

    private static void ObserveDomain(
        ProjectDocument document,
        ulong[] observations,
        CancellationToken cancellationToken)
    {
        var maximumMemoryPartBytes = 0UL;
        var expandedMemoryBytes = 0UL;
        var memoryCellCount = 0UL;
        foreach (var image in document.MemoryImages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cells = checked((ulong)image.Width * image.Depth);
            var partBytes = SaturatingAdd(20, SaturatingAdd(cells, 3) / 4);
            maximumMemoryPartBytes = Math.Max(maximumMemoryPartBytes, partBytes);
            expandedMemoryBytes = SaturatingAdd(expandedMemoryBytes, partBytes);
            memoryCellCount = SaturatingAdd(memoryCellCount, cells);
        }

        observations[(int)PackageDimension.EntryCount] =
            checked((ulong)document.MemoryImages.Count + 2);
        observations[(int)PackageDimension.PartBytes] = maximumMemoryPartBytes;
        observations[(int)PackageDimension.ExpandedBytes] = expandedMemoryBytes;
        observations[(int)PackageDimension.EntityCount] = ObserveEntities(
            document,
            cancellationToken);
        observations[(int)PackageDimension.MemoryPartCount] =
            checked((ulong)document.MemoryImages.Count);
        observations[(int)PackageDimension.MemoryCellCount] = memoryCellCount;
    }

    private static ulong ObserveEntities(
        ProjectDocument document,
        CancellationToken cancellationToken)
    {
        var count = 1UL;
        count = SaturatingAdd(count, checked((ulong)document.CircuitDefinitions.Count));
        count = SaturatingAdd(count, checked((ulong)document.MemoryImages.Count));
        foreach (var definition in document.CircuitDefinitions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            count = SaturatingAdd(count, checked((ulong)definition.Ports.Count));
            count = SaturatingAdd(count, checked((ulong)definition.ComponentInstances.Count));
            count = SaturatingAdd(count, checked((ulong)definition.Nets.Count));
            count = SaturatingAdd(count, checked((ulong)definition.Junctions.Count));
            count = SaturatingAdd(count, checked((ulong)definition.WireGeometries.Count));
            count = SaturatingAdd(count, checked((ulong)definition.Annotations.Count));
        }

        return count;
    }

    private static PackageDimensionObservation? FindBreach(
        PackagePolicy policy,
        ulong[] observations,
        bool includeCarrier)
    {
        foreach (var limit in policy.Limits)
        {
            if (!includeCarrier
                && limit.Dimension == PackageDimension.CarrierBytes)
            {
                continue;
            }

            var observed = observations[(int)limit.Dimension];
            if (observed > limit.Maximum)
            {
                return new PackageDimensionObservation(
                    limit.Dimension,
                    observed);
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
                new("dimension", breach.GetDimensionToken()),
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
            Array.AsReadOnly(policy.Limits
                .Select(limit => new PackageDimensionObservation(
                    limit.Dimension,
                    observations[(int)limit.Dimension]))
                .ToArray()),
            breach);

    private static bool IsCooperativeCancellation(
        OperationCanceledException exception,
        CancellationToken cancellationToken) =>
        cancellationToken.IsCancellationRequested
        && exception.CancellationToken == cancellationToken;

    private static bool IsFatal(Exception exception) => exception is
        OutOfMemoryException or StackOverflowException or AccessViolationException;

    private static ulong SaturatingAdd(ulong left, ulong right) =>
        ulong.MaxValue - left < right ? ulong.MaxValue : left + right;

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
            string? memoryImageId,
            CancellationToken cancellationToken)
        {
            using var hashing = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            for (var offset = 0; offset < bytes.Length; offset += 64 * 1_024)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var length = Math.Min(64 * 1_024, bytes.Length - offset);
                hashing.AppendData(bytes, offset, length);
            }

            var hash = hashing.GetHashAndReset();
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

        public override bool CanWrite
        {
            get
            {
                try
                {
                    return destination.CanWrite;
                }
                catch (Exception exception) when (!IsFatal(exception))
                {
                    throw new DestinationWriteException(exception);
                }
            }
        }

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
            try
            {
                await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException exception)
                when (IsCooperativeCancellation(exception, cancellationToken))
            {
                throw;
            }
            catch (Exception exception) when (!IsFatal(exception))
            {
                throw new DestinationWriteException(exception);
            }
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
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            deferredSynchronousWrites.Write(buffer);
        }

        public override async ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            await FlushDeferredWritesAsync(cancellationToken).ConfigureAwait(false);
            await WriteToDestinationAsync(buffer, cancellationToken).ConfigureAwait(false);
        }

        public override async Task WriteAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            await FlushDeferredWritesAsync(cancellationToken).ConfigureAwait(false);
            await WriteToDestinationAsync(
                buffer.AsMemory(offset, count),
                cancellationToken).ConfigureAwait(false);
        }

        private async Task FlushDeferredWritesAsync(
            CancellationToken cancellationToken)
        {
            if (deferredSynchronousWrites.WrittenCount == 0)
            {
                return;
            }

            await WriteToDestinationAsync(
                deferredSynchronousWrites.WrittenMemory,
                cancellationToken).ConfigureAwait(false);
            deferredSynchronousWrites.Clear();
        }

        private async ValueTask WriteToDestinationAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken)
        {
            try
            {
                await destination.WriteAsync(buffer, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException exception)
                when (IsCooperativeCancellation(exception, cancellationToken))
            {
                throw;
            }
            catch (Exception exception) when (!IsFatal(exception))
            {
                throw new DestinationWriteException(exception);
            }

            BytesWritten = SaturatingAdd(
                BytesWritten,
                checked((ulong)buffer.Length));
        }
    }

    private sealed class DestinationWriteException(Exception innerException) :
        Exception("The project package destination failed.", innerException);
}
