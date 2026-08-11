using System.Buffers;
using System.Text;
using System.Text.Json;

namespace LogicLab.ProjectFormat;

internal sealed class CanonicalJsonWriter(
    Utf8JsonWriter writer,
    ulong[] observations,
    CancellationToken cancellationToken)
{
    private const int CancellationInterval = 1_024;
    private readonly List<bool> containers = [];
    private int workSinceCancellation;

    public CancellationToken CancellationToken => cancellationToken;

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
        ObserveValueToken();
        WriteStringValueCore(value);
    }

    public void WriteStringValue(string value)
    {
        ObserveValueToken();
        WriteStringValueCore(value);
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
        var buffer = new ArrayBufferWriter<byte>(checked(value.Length + 2));
        buffer.Write("\""u8);
        Span<byte> utf8 = stackalloc byte[4];
        Span<byte> escapedControl = stackalloc byte[6];
        "\\u0000"u8.CopyTo(escapedControl);
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

            switch (rune.Value)
            {
                case '"':
                    buffer.Write("\\\""u8);
                    break;
                case '\\':
                    buffer.Write("\\\\"u8);
                    break;
                case '\b':
                    buffer.Write("\\b"u8);
                    break;
                case '\t':
                    buffer.Write("\\t"u8);
                    break;
                case '\n':
                    buffer.Write("\\n"u8);
                    break;
                case '\f':
                    buffer.Write("\\f"u8);
                    break;
                case '\r':
                    buffer.Write("\\r"u8);
                    break;
                case < 0x20:
                    WriteEscapedControl(buffer, escapedControl, rune.Value);
                    break;
                default:
                    if (!rune.TryEncodeToUtf8(utf8, out var bytesWritten))
                    {
                        throw new InvalidOperationException(
                            "A Unicode scalar could not be encoded as UTF-8.");
                    }

                    buffer.Write(utf8[..bytesWritten]);
                    break;
            }

            ObserveScalar(rune);
            remaining = remaining[charactersConsumed..];
        }

        buffer.Write("\""u8);
        writer.WriteRawValue(buffer.WrittenSpan, skipInputValidation: true);
    }

    private static void WriteEscapedControl(
        ArrayBufferWriter<byte> buffer,
        Span<byte> escapedControl,
        int value)
    {
        ReadOnlySpan<byte> hexadecimal = "0123456789abcdef"u8;
        escapedControl[4] = hexadecimal[value >> 4];
        escapedControl[5] = hexadecimal[value & 0x0f];
        buffer.Write(escapedControl);
    }

    private void ObserveProperty(string propertyName)
    {
        ObserveToken();
        ObserveDecodedString(propertyName);
    }

    private void ObserveValueToken()
    {
        if (containers.Count > 0 && containers[^1])
        {
            observations[(int)PackageDimension.ArrayItems] = SaturatingAdd(
                observations[(int)PackageDimension.ArrayItems],
                1);
        }

        ObserveToken();
    }

    private void ObserveToken()
    {
        Checkpoint();
        observations[(int)PackageDimension.JsonTokens] = SaturatingAdd(
            observations[(int)PackageDimension.JsonTokens],
            1);
        observations[(int)PackageDimension.JsonDepth] = Math.Max(
            observations[(int)PackageDimension.JsonDepth],
            checked((ulong)containers.Count + 1));
    }

    private void ObserveDecodedString(string value)
    {
        foreach (var rune in value.EnumerateRunes())
        {
            Checkpoint();
            ObserveScalar(rune);
        }
    }

    private void ObserveScalar(Rune rune)
    {
        observations[(int)PackageDimension.StringScalarCount] =
            SaturatingAdd(
                observations[(int)PackageDimension.StringScalarCount],
                1);
        observations[(int)PackageDimension.StringUtf8Bytes] =
            SaturatingAdd(
                observations[(int)PackageDimension.StringUtf8Bytes],
                checked((ulong)rune.Utf8SequenceLength));
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
