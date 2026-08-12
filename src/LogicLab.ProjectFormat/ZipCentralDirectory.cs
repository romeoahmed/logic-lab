using System.Buffers.Binary;

namespace LogicLab.ProjectFormat;

internal static class ZipCentralDirectory
{
    private const uint CentralDirectoryEntrySignature = 0x02014b50;
    private const uint EndOfCentralDirectorySignature = 0x06054b50;
    private const uint LocalFileHeaderSignature = 0x04034b50;
    private const uint Zip64EndOfCentralDirectorySignature = 0x06064b50;
    private const uint Zip64EndOfCentralDirectoryLocatorSignature = 0x07064b50;

    public static async Task<ZipCentralDirectoryInfo> ReadInfoAsync(
        FileStream spool,
        CancellationToken cancellationToken)
    {
        var location = await ReadLocationAsync(spool, cancellationToken)
            .ConfigureAwait(false);
        return new ZipCentralDirectoryInfo(
            location.EntryCount,
            location.Offset,
            location.Length);
    }

    public static async Task<ZipUnsupportedFeature?> FindUnsupportedFeatureAsync(
        FileStream spool,
        ZipCentralDirectoryInfo directory,
        CancellationToken cancellationToken)
    {
        const int headerLength = 46;
        const ushort encryptionFlags = 0x2041;
        ZipUnsupportedFeature? unsupportedFeature = null;
        var header = new byte[headerLength];
        var localHeader = new byte[30];
        var directoryEnd = checked(directory.Offset + directory.Length);
        if (directory.Offset > checked((ulong)spool.Length)
            || directoryEnd > checked((ulong)spool.Length))
        {
            throw new InvalidDataException("The ZIP central directory is outside the carrier.");
        }

        spool.Position = checked((long)directory.Offset);
        for (ulong index = 0; index < directory.EntryCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (checked((ulong)spool.Position + headerLength) > directoryEnd)
            {
                throw new InvalidDataException("The ZIP central directory is truncated.");
            }

            await spool.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
            if (BinaryPrimitives.ReadUInt32LittleEndian(header) !=
                CentralDirectoryEntrySignature)
            {
                throw new InvalidDataException("The ZIP central directory entry is invalid.");
            }

            var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(28));
            var extraLength = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(30));
            var commentLength = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(32));
            var variableLength = checked(nameLength + extraLength + commentLength);
            if (checked((ulong)spool.Position + (ulong)variableLength) > directoryEnd)
            {
                throw new InvalidDataException("The ZIP central directory entry is truncated.");
            }

            var variableData = new byte[variableLength];
            await spool.ReadExactlyAsync(variableData, cancellationToken)
                .ConfigureAwait(false);
            var localHeaderOffset = ResolveLocalHeaderOffset(
                header,
                variableData.AsSpan(nameLength, extraLength));
            if (ResolveDiskStart(
                    header,
                    variableData.AsSpan(nameLength, extraLength)) != 0)
            {
                throw new InvalidDataException("Split ZIP archives are unsupported.");
            }

            var nextCentralEntry = spool.Position;
            if (spool.Length < localHeader.Length
                || localHeaderOffset > checked(
                    (ulong)(spool.Length - localHeader.Length)))
            {
                throw new InvalidDataException("The ZIP local header is outside the carrier.");
            }

            spool.Position = checked((long)localHeaderOffset);
            await spool.ReadExactlyAsync(localHeader, cancellationToken)
                .ConfigureAwait(false);
            if (BinaryPrimitives.ReadUInt32LittleEndian(localHeader) !=
                LocalFileHeaderSignature)
            {
                throw new InvalidDataException("The ZIP local header is invalid.");
            }

            var centralCompressionMethod =
                BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(10));
            var localCompressionMethod =
                BinaryPrimitives.ReadUInt16LittleEndian(localHeader.AsSpan(8));
            var centralFlags = BinaryPrimitives.ReadUInt16LittleEndian(
                header.AsSpan(8));
            var localFlags = BinaryPrimitives.ReadUInt16LittleEndian(
                localHeader.AsSpan(6));
            if ((centralFlags & encryptionFlags) != 0
                || (localFlags & encryptionFlags) != 0)
            {
                unsupportedFeature = ZipUnsupportedFeature.Encryption;
            }
            else if (centralCompressionMethod is not 0 and not 8
                || localCompressionMethod is not 0 and not 8)
            {
                unsupportedFeature ??= ZipUnsupportedFeature.Compression;
            }
            else if (centralFlags != localFlags
                || centralCompressionMethod != localCompressionMethod)
            {
                throw new InvalidDataException(
                    "The ZIP local and central headers are inconsistent.");
            }

            spool.Position = nextCentralEntry;
        }

        if (checked((ulong)spool.Position) != directoryEnd)
        {
            throw new InvalidDataException("The ZIP central directory count is inconsistent.");
        }

        return unsupportedFeature;
    }

    private static uint ResolveDiskStart(
        byte[] header,
        ReadOnlySpan<byte> extraFields)
    {
        var diskStart = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(34));
        if (diskStart != ushort.MaxValue)
        {
            return diskStart;
        }

        var zip64 = FindExtraField(extraFields, 0x0001);
        var offset = 0;
        if (BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(24)) == uint.MaxValue)
        {
            offset = checked(offset + sizeof(ulong));
        }

        if (BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(20)) == uint.MaxValue)
        {
            offset = checked(offset + sizeof(ulong));
        }

        if (BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(42)) == uint.MaxValue)
        {
            offset = checked(offset + sizeof(ulong));
        }

        if (zip64.Length < checked(offset + sizeof(uint)))
        {
            throw new InvalidDataException("The ZIP64 disk start is missing.");
        }

        return BinaryPrimitives.ReadUInt32LittleEndian(zip64[offset..]);
    }

    private static ulong ResolveLocalHeaderOffset(
        byte[] header,
        ReadOnlySpan<byte> extraFields)
    {
        var localHeaderOffset = BinaryPrimitives.ReadUInt32LittleEndian(
            header.AsSpan(42));
        if (localHeaderOffset != uint.MaxValue)
        {
            return localHeaderOffset;
        }

        var zip64 = FindExtraField(extraFields, 0x0001);
        var offset = 0;
        if (BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(24)) == uint.MaxValue)
        {
            offset = checked(offset + sizeof(ulong));
        }

        if (BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(20)) == uint.MaxValue)
        {
            offset = checked(offset + sizeof(ulong));
        }

        if (zip64.Length < checked(offset + sizeof(ulong)))
        {
            throw new InvalidDataException("The ZIP64 local header offset is missing.");
        }

        return BinaryPrimitives.ReadUInt64LittleEndian(zip64[offset..]);
    }

    private static ReadOnlySpan<byte> FindExtraField(
        ReadOnlySpan<byte> extraFields,
        ushort expectedId)
    {
        while (extraFields.Length >= 4)
        {
            var id = BinaryPrimitives.ReadUInt16LittleEndian(extraFields);
            var length = BinaryPrimitives.ReadUInt16LittleEndian(extraFields[2..]);
            if (extraFields.Length < checked(4 + length))
            {
                throw new InvalidDataException("The ZIP extra field is truncated.");
            }

            if (id == expectedId)
            {
                return extraFields.Slice(4, length);
            }

            extraFields = extraFields[(4 + length)..];
        }

        if (!extraFields.IsEmpty)
        {
            throw new InvalidDataException("The ZIP extra field is truncated.");
        }

        throw new InvalidDataException("The ZIP64 extra field is missing.");
    }

    private static async Task<CentralDirectoryLocation> ReadLocationAsync(
        FileStream spool,
        CancellationToken cancellationToken)
    {
        const int endRecordLength = 22;
        const int maximumCommentLength = ushort.MaxValue;
        var tailLength = checked((int)Math.Min(
            spool.Length,
            endRecordLength + maximumCommentLength));
        if (tailLength < endRecordLength)
        {
            throw new InvalidDataException("The ZIP end record is missing.");
        }

        var tail = new byte[tailLength];
        spool.Position = spool.Length - tailLength;
        await spool.ReadExactlyAsync(tail, cancellationToken).ConfigureAwait(false);
        var endRecordIndex = FindEndOfCentralDirectory(tail);
        if (endRecordIndex < 0)
        {
            throw new InvalidDataException("The ZIP end record is missing.");
        }

        var endRecord = tail.AsSpan(endRecordIndex);
        var diskNumber = BinaryPrimitives.ReadUInt16LittleEndian(endRecord[4..]);
        var centralDirectoryDisk = BinaryPrimitives.ReadUInt16LittleEndian(endRecord[6..]);
        var entriesOnDisk = BinaryPrimitives.ReadUInt16LittleEndian(endRecord[8..]);
        var totalEntries = BinaryPrimitives.ReadUInt16LittleEndian(endRecord[10..]);
        if (diskNumber != 0
            || centralDirectoryDisk != 0
            || entriesOnDisk != totalEntries)
        {
            throw new InvalidDataException("Split ZIP archives are unsupported.");
        }

        var directoryLength = BinaryPrimitives.ReadUInt32LittleEndian(endRecord[12..]);
        var directoryOffset = BinaryPrimitives.ReadUInt32LittleEndian(endRecord[16..]);
        var endRecordOffset = checked(spool.Length - tailLength + endRecordIndex);
        return entriesOnDisk == ushort.MaxValue
            || totalEntries == ushort.MaxValue
            || directoryLength == uint.MaxValue
            || directoryOffset == uint.MaxValue
                ? await ReadZip64LocationAsync(
                    spool,
                    endRecordOffset,
                    cancellationToken).ConfigureAwait(false)
                : new CentralDirectoryLocation(
                    totalEntries,
                    directoryOffset,
                    directoryLength);
    }

    private static async Task<CentralDirectoryLocation> ReadZip64LocationAsync(
        FileStream spool,
        long endRecordOffset,
        CancellationToken cancellationToken)
    {
        const int locatorLength = 20;
        const int zip64EndRecordMinimumLength = 56;
        if (endRecordOffset < locatorLength)
        {
            throw new InvalidDataException("The ZIP64 locator is missing.");
        }

        var locator = new byte[locatorLength];
        spool.Position = endRecordOffset - locatorLength;
        await spool.ReadExactlyAsync(locator, cancellationToken).ConfigureAwait(false);
        if (BinaryPrimitives.ReadUInt32LittleEndian(locator) !=
            Zip64EndOfCentralDirectoryLocatorSignature)
        {
            throw new InvalidDataException("The ZIP64 locator is missing.");
        }

        if (BinaryPrimitives.ReadUInt32LittleEndian(locator.AsSpan(4)) != 0
            || BinaryPrimitives.ReadUInt32LittleEndian(locator.AsSpan(16)) != 1)
        {
            throw new InvalidDataException("Split ZIP64 archives are unsupported.");
        }

        var zip64EndRecordOffset = BinaryPrimitives.ReadUInt64LittleEndian(
            locator.AsSpan(8));
        if (spool.Length < zip64EndRecordMinimumLength
            || zip64EndRecordOffset > checked(
                (ulong)(spool.Length - zip64EndRecordMinimumLength)))
        {
            throw new InvalidDataException("The ZIP64 end record offset is invalid.");
        }

        var record = new byte[zip64EndRecordMinimumLength];
        spool.Position = checked((long)zip64EndRecordOffset);
        await spool.ReadExactlyAsync(record, cancellationToken).ConfigureAwait(false);
        if (BinaryPrimitives.ReadUInt32LittleEndian(record) !=
            Zip64EndOfCentralDirectorySignature
            || BinaryPrimitives.ReadUInt64LittleEndian(record.AsSpan(4)) < 44)
        {
            throw new InvalidDataException("The ZIP64 end record is invalid.");
        }

        var diskNumber = BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(16));
        var centralDirectoryDisk = BinaryPrimitives.ReadUInt32LittleEndian(
            record.AsSpan(20));
        var entriesOnDisk = BinaryPrimitives.ReadUInt64LittleEndian(record.AsSpan(24));
        var totalEntries = BinaryPrimitives.ReadUInt64LittleEndian(record.AsSpan(32));
        if (diskNumber != 0
            || centralDirectoryDisk != 0
            || entriesOnDisk != totalEntries)
        {
            throw new InvalidDataException("Split ZIP64 archives are unsupported.");
        }

        return new CentralDirectoryLocation(
            totalEntries,
            BinaryPrimitives.ReadUInt64LittleEndian(record.AsSpan(48)),
            BinaryPrimitives.ReadUInt64LittleEndian(record.AsSpan(40)));
    }

    private static int FindEndOfCentralDirectory(byte[] bytes)
    {
        const int endRecordLength = 22;
        for (var index = bytes.Length - sizeof(uint); index >= 0; index--)
        {
            if (bytes.Length - index >= endRecordLength
                && BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(index)) ==
                    EndOfCentralDirectorySignature)
            {
                var commentLength = BinaryPrimitives.ReadUInt16LittleEndian(
                    bytes.AsSpan(index + 20));
                if (checked(index + endRecordLength + commentLength) == bytes.Length)
                {
                    return index;
                }
            }
        }

        return -1;
    }

    private sealed record CentralDirectoryLocation(
        ulong EntryCount,
        ulong Offset,
        ulong Length);
}

internal sealed record ZipCentralDirectoryInfo(
    ulong EntryCount,
    ulong Offset,
    ulong Length);

internal enum ZipUnsupportedFeature
{
    Compression,
    Encryption,
}
