using System.Buffers.Binary;

namespace LogicLab.ProjectFormat;

internal static class ZipCentralDirectory
{
    private const uint EndOfCentralDirectorySignature = 0x06054b50;
    private const uint Zip64EndOfCentralDirectorySignature = 0x06064b50;
    private const uint Zip64EndOfCentralDirectoryLocatorSignature = 0x07064b50;

    public static async Task<ulong> ReadDeclaredEntryCountAsync(
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
        if (diskNumber != centralDirectoryDisk || entriesOnDisk != totalEntries)
        {
            throw new InvalidDataException("Split ZIP archives are unsupported.");
        }

        var endRecordOffset = checked(spool.Length - tailLength + endRecordIndex);
        return entriesOnDisk == ushort.MaxValue
            || totalEntries == ushort.MaxValue
            || BinaryPrimitives.ReadUInt32LittleEndian(endRecord[16..]) == uint.MaxValue
            ? await ReadZip64DeclaredEntryCountAsync(
                spool,
                endRecordOffset,
                totalEntries,
                cancellationToken).ConfigureAwait(false)
            : totalEntries;
    }

    private static async Task<ulong> ReadZip64DeclaredEntryCountAsync(
        FileStream spool,
        long endRecordOffset,
        ulong fallback,
        CancellationToken cancellationToken)
    {
        const int locatorLength = 20;
        const int zip64EndRecordMinimumLength = 56;
        if (endRecordOffset < locatorLength)
        {
            return fallback;
        }

        var locator = new byte[locatorLength];
        spool.Position = endRecordOffset - locatorLength;
        await spool.ReadExactlyAsync(locator, cancellationToken).ConfigureAwait(false);
        if (BinaryPrimitives.ReadUInt32LittleEndian(locator) !=
            Zip64EndOfCentralDirectoryLocatorSignature)
        {
            return fallback;
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
        if (diskNumber != centralDirectoryDisk || entriesOnDisk != totalEntries)
        {
            throw new InvalidDataException("Split ZIP64 archives are unsupported.");
        }

        return totalEntries;
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
}
