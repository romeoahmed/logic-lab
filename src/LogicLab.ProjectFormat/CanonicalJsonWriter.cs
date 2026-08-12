using System.Buffers;
using System.Text;
using System.Text.Json;

namespace LogicLab.ProjectFormat;

internal sealed class CanonicalJsonWriter
{
    private const int CancellationInterval = 1_024;
    private static ReadOnlySpan<byte> PlaceholderValue => "0"u8;
    private readonly Utf8JsonWriter writer;
    private readonly ulong[]? observations;
    private readonly PackagePolicy? policy;
    private readonly CancellationToken cancellationToken;
    private readonly bool measureOnly;
    private readonly List<bool> containers = [];
    private int workSinceCancellation;

    public CanonicalJsonWriter(
        Utf8JsonWriter writer,
        ulong[]? observations,
        PackagePolicy? policy,
        bool measureOnly,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(writer);
        if ((observations is null) != (policy is null))
        {
            throw new ArgumentException(
                "JSON observations and their Package Policy must be supplied together.");
        }

        this.writer = writer;
        this.observations = observations;
        this.policy = policy;
        this.cancellationToken = cancellationToken;
        this.measureOnly = measureOnly;
    }

    public CancellationToken CancellationToken => cancellationToken;

    public ulong RawValueByteAdjustment { get; private set; }

    public void WriteStartObject()
    {
        ObserveValueToken();
        writer.WriteStartObject();
        containers.Add(false);
    }

    public void WriteEndObject()
    {
        containers.RemoveAt(containers.Count - 1);
        ObserveToken();
        writer.WriteEndObject();
    }

    public void WriteStartArray()
    {
        ObserveValueToken();
        writer.WriteStartArray();
        containers.Add(true);
    }

    public void WriteEndArray()
    {
        containers.RemoveAt(containers.Count - 1);
        ObserveToken();
        writer.WriteEndArray();
    }

    public void WritePropertyName(string propertyName)
    {
        ObserveProperty(propertyName);
        writer.WritePropertyName(propertyName);
    }

    public void WriteString(string propertyName, string value)
    {
        ObserveProperty(propertyName);
        writer.WritePropertyName(propertyName);
        WriteStringValue(value);
    }

    public void WriteStringValue(string value)
    {
        ObserveValueToken();
        WriteStringValueCore(value);
    }

    public void WriteUnescapedAsciiStringValue(
        int length,
        Func<int, byte> valueAt)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        ArgumentNullException.ThrowIfNull(valueAt);
        ObserveValueToken();
        var encodedLength = checked((ulong)length + 2);
        if (measureOnly)
        {
            for (var index = 0; index < length; index++)
            {
                Checkpoint();
                var value = valueAt(index);
                EnsureUnescapedAscii(value);
                ObserveScalar(new Rune(value));
            }

            WriteMeasuredString(encodedLength);
            return;
        }

        var bytes = GC.AllocateUninitializedArray<byte>(
            checked((int)encodedLength));
        bytes[0] = (byte)'"';
        for (var index = 0; index < length; index++)
        {
            Checkpoint();
            var value = valueAt(index);
            EnsureUnescapedAscii(value);
            bytes[index + 1] = value;
        }

        bytes[^1] = (byte)'"';
        writer.WriteRawValue(bytes, skipInputValidation: true);
    }

    public void WriteNull(string propertyName)
    {
        ObserveProperty(propertyName);
        ObserveValueToken();
        writer.WriteNull(propertyName);
    }

    public void WriteBoolean(string propertyName, bool value)
    {
        ObserveProperty(propertyName);
        ObserveValueToken();
        writer.WriteBoolean(propertyName, value);
    }

    public void WriteNumber(string propertyName, int value)
    {
        ObserveProperty(propertyName);
        ObserveValueToken();
        writer.WriteNumber(propertyName, value);
    }

    public void WriteNumber(string propertyName, uint value)
    {
        ObserveProperty(propertyName);
        ObserveValueToken();
        writer.WriteNumber(propertyName, value);
    }

    public void WriteNumber(string propertyName, long value)
    {
        ObserveProperty(propertyName);
        ObserveValueToken();
        writer.WriteNumber(propertyName, value);
    }

    public void WriteNumber(string propertyName, ulong value)
    {
        ObserveProperty(propertyName);
        ObserveValueToken();
        writer.WriteNumber(propertyName, value);
    }

    public void WriteNumberValue(uint value)
    {
        ObserveValueToken();
        writer.WriteNumberValue(value);
    }

    private void WriteStringValueCore(string value)
    {
        var encodedLength = MeasureString(value);
        if (measureOnly)
        {
            WriteMeasuredString(encodedLength);
            return;
        }

        var bytes = GC.AllocateUninitializedArray<byte>(
            checked((int)encodedLength));
        EncodeString(value, bytes);
        writer.WriteRawValue(bytes, skipInputValidation: true);
    }

    private ulong MeasureString(string value)
    {
        var encodedLength = 2UL;
        var remaining = value.AsSpan();
        while (!remaining.IsEmpty)
        {
            Checkpoint();
            var status = Rune.DecodeFromUtf16(
                remaining,
                out var rune,
                out var charactersConsumed);
            if (status != OperationStatus.Done)
            {
                throw new InvalidOperationException(
                    "A canonical JSON string must contain only Unicode scalar values.");
            }

            ObserveScalar(rune);
            encodedLength = checked(encodedLength + EncodedLength(rune));
            remaining = remaining[charactersConsumed..];
        }

        return encodedLength;
    }

    private void EncodeString(string value, Span<byte> destination)
    {
        var offset = 0;
        destination[offset++] = (byte)'"';
        var remaining = value.AsSpan();
        while (!remaining.IsEmpty)
        {
            Checkpoint();
            var status = Rune.DecodeFromUtf16(
                remaining,
                out var rune,
                out var charactersConsumed);
            if (status != OperationStatus.Done)
            {
                throw new InvalidOperationException(
                    "A canonical JSON string must contain only Unicode scalar values.");
            }

            offset += EncodeRune(rune, destination[offset..]);
            remaining = remaining[charactersConsumed..];
        }

        destination[offset++] = (byte)'"';
        if (offset != destination.Length)
        {
            throw new InvalidOperationException(
                "Canonical JSON string measurement and encoding diverged.");
        }
    }

    private void WriteMeasuredString(ulong encodedLength)
    {
        writer.WriteRawValue(PlaceholderValue, skipInputValidation: true);
        RawValueByteAdjustment = checked(
            RawValueByteAdjustment + encodedLength - 1);
    }

    private static ulong EncodedLength(Rune rune) => rune.Value switch
    {
        '"' or '\\' or '\b' or '\t' or '\n' or '\f' or '\r' => 2,
        < 0x20 => 6,
        _ => checked((ulong)rune.Utf8SequenceLength),
    };

    private static int EncodeRune(Rune rune, Span<byte> destination)
    {
        switch (rune.Value)
        {
            case '"':
                "\\\""u8.CopyTo(destination);
                return 2;
            case '\\':
                "\\\\"u8.CopyTo(destination);
                return 2;
            case '\b':
                "\\b"u8.CopyTo(destination);
                return 2;
            case '\t':
                "\\t"u8.CopyTo(destination);
                return 2;
            case '\n':
                "\\n"u8.CopyTo(destination);
                return 2;
            case '\f':
                "\\f"u8.CopyTo(destination);
                return 2;
            case '\r':
                "\\r"u8.CopyTo(destination);
                return 2;
            case < 0x20:
                WriteEscapedControl(destination, rune.Value);
                return 6;
            default:
                if (!rune.TryEncodeToUtf8(destination, out var bytesWritten))
                {
                    throw new InvalidOperationException(
                        "A Unicode scalar could not be encoded as UTF-8.");
                }

                return bytesWritten;
        }
    }

    private static void WriteEscapedControl(Span<byte> destination, int value)
    {
        ReadOnlySpan<byte> hexadecimal = "0123456789abcdef"u8;
        "\\u0000"u8.CopyTo(destination);
        destination[4] = hexadecimal[value >> 4];
        destination[5] = hexadecimal[value & 0x0f];
    }

    private static void EnsureUnescapedAscii(byte value)
    {
        if (value is < 0x20 or >= 0x7f or (byte)'"' or (byte)'\\')
        {
            throw new InvalidOperationException(
                "An unescaped canonical JSON string must contain printable safe ASCII.");
        }
    }

    private void ObserveProperty(string propertyName)
    {
        ObserveToken();
        ObserveDecodedString(propertyName);
    }

    private void ObserveValueToken()
    {
        if (observations is not null && containers.Count > 0 && containers[^1])
        {
            observations[(int)PackageDimension.ArrayItems] = SaturatingAdd(
                observations[(int)PackageDimension.ArrayItems],
                1);
        }

        ObserveToken();
        ThrowIfExceeded(PackageDimension.ArrayItems);
    }

    private void ObserveToken()
    {
        Checkpoint();
        if (observations is null)
        {
            return;
        }

        observations[(int)PackageDimension.JsonTokens] = SaturatingAdd(
            observations[(int)PackageDimension.JsonTokens],
            1);
        observations[(int)PackageDimension.JsonDepth] = Math.Max(
            observations[(int)PackageDimension.JsonDepth],
            checked((ulong)containers.Count + 1));
        ThrowIfExceeded(PackageDimension.JsonDepth);
        ThrowIfExceeded(PackageDimension.JsonTokens);
    }

    private void ObserveDecodedString(string value)
    {
        var remaining = value.AsSpan();
        while (!remaining.IsEmpty)
        {
            Checkpoint();
            var status = Rune.DecodeFromUtf16(
                remaining,
                out var rune,
                out var charactersConsumed);
            if (status != OperationStatus.Done)
            {
                throw new InvalidOperationException(
                    "A canonical JSON string must contain only Unicode scalar values.");
            }

            ObserveScalar(rune);
            remaining = remaining[charactersConsumed..];
        }
    }

    private void ObserveScalar(Rune rune)
    {
        if (observations is null)
        {
            return;
        }

        observations[(int)PackageDimension.StringScalarCount] = SaturatingAdd(
            observations[(int)PackageDimension.StringScalarCount],
            1);
        observations[(int)PackageDimension.StringUtf8Bytes] = SaturatingAdd(
            observations[(int)PackageDimension.StringUtf8Bytes],
            checked((ulong)rune.Utf8SequenceLength));
        ThrowIfExceeded(PackageDimension.StringScalarCount);
        ThrowIfExceeded(PackageDimension.StringUtf8Bytes);
    }

    private void ThrowIfExceeded(PackageDimension dimension)
    {
        if (observations is null || policy is null)
        {
            return;
        }

        var observed = observations[(int)dimension];
        if (observed > policy.GetMaximum(dimension))
        {
            throw new PackagePolicyLimitException(
                new PackageDimensionObservation(dimension, observed));
        }
    }

    private void Checkpoint()
    {
        workSinceCancellation++;
        if (workSinceCancellation != CancellationInterval)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        workSinceCancellation = 0;
    }

    private static ulong SaturatingAdd(ulong left, ulong right) =>
        ulong.MaxValue - left < right ? ulong.MaxValue : left + right;
}

internal static class CanonicalJson
{
    public static ulong Measure(
        Action<CanonicalJsonWriter> write,
        ulong[] observations,
        PackagePolicy policy,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(write);
        cancellationToken.ThrowIfCancellationRequested();
        var sink = new CountingBufferWriter();
        using var utf8Writer = CreateWriter(sink);
        var writer = new CanonicalJsonWriter(
            utf8Writer,
            observations,
            policy,
            measureOnly: true,
            cancellationToken);
        write(writer);
        utf8Writer.Flush();
        return checked(
            sink.WrittenCount + writer.RawValueByteAdjustment + 1);
    }

    public static byte[] Write(
        Action<CanonicalJsonWriter> write,
        ulong measuredByteCount,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(write);
        cancellationToken.ThrowIfCancellationRequested();
        var byteCount = checked((int)measuredByteCount);
        if (byteCount == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(measuredByteCount));
        }

        var bytes = GC.AllocateUninitializedArray<byte>(byteCount);
        var sink = new FixedBufferWriter(bytes.AsMemory(0, byteCount - 1));
        using var utf8Writer = CreateWriter(sink);
        var writer = new CanonicalJsonWriter(
            utf8Writer,
            observations: null,
            policy: null,
            measureOnly: false,
            cancellationToken);
        write(writer);
        utf8Writer.Flush();
        if (sink.WrittenCount != byteCount - 1)
        {
            throw new InvalidOperationException(
                "Canonical JSON measurement and writing diverged.");
        }

        bytes[^1] = (byte)'\n';
        return bytes;
    }

    private static Utf8JsonWriter CreateWriter(IBufferWriter<byte> sink) =>
        new(
            sink,
            new JsonWriterOptions
            {
                Indented = false,
                SkipValidation = false,
            });

    private abstract class ScratchBufferWriter : IBufferWriter<byte>
    {
        private byte[] buffer = new byte[4 * 1_024];

        public abstract void Advance(int count);

        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            EnsureCapacity(sizeHint);
            return buffer;
        }

        public Span<byte> GetSpan(int sizeHint = 0)
        {
            EnsureCapacity(sizeHint);
            return buffer;
        }

        private void EnsureCapacity(int sizeHint)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(sizeHint);
            if (sizeHint > buffer.Length)
            {
                Array.Resize(ref buffer, sizeHint);
            }
        }

        protected ReadOnlySpan<byte> WrittenSpan(int count)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(count, buffer.Length);
            return buffer.AsSpan(0, count);
        }
    }

    private sealed class CountingBufferWriter : ScratchBufferWriter
    {
        public ulong WrittenCount { get; private set; }

        public override void Advance(int count)
        {
            _ = WrittenSpan(count);
            WrittenCount = checked(WrittenCount + (ulong)count);
        }
    }

    private sealed class FixedBufferWriter(Memory<byte> destination) :
        ScratchBufferWriter
    {
        public int WrittenCount { get; private set; }

        public override void Advance(int count)
        {
            var written = WrittenSpan(count);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(
                count,
                destination.Length - WrittenCount);

            written.CopyTo(destination.Span[WrittenCount..]);
            WrittenCount += count;
        }
    }
}

internal sealed class PackagePolicyLimitException(
    PackageDimensionObservation breach) : Exception
{
    public PackageDimensionObservation Breach { get; } = breach;
}
