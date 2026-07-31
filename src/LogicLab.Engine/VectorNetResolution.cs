namespace LogicLab.Engine;

public sealed class VectorNetResolution
{
    private readonly ulong[] undrivenBits;
    private readonly ulong[] unknownDriverBits;
    private readonly ulong[] contentionBits;

    internal VectorNetResolution(
        LogicVector value,
        ulong[] undrivenBits,
        ulong[] unknownDriverBits,
        ulong[] contentionBits)
    {
        Value = value;
        this.undrivenBits = undrivenBits;
        this.unknownDriverBits = unknownDriverBits;
        this.contentionBits = contentionBits;
    }

    public LogicVector Value { get; }

    public NetResolutionCauses GetCauses(int bitIndex)
    {
        if (bitIndex < 0 || bitIndex >= Value.Width)
        {
            throw new ArgumentOutOfRangeException(nameof(bitIndex));
        }

        var wordIndex = bitIndex / LogicVector.BitsPerWord;
        var bitMask = 1UL << (bitIndex % LogicVector.BitsPerWord);
        var causes = NetResolutionCauses.None;

        if ((undrivenBits[wordIndex] & bitMask) != 0)
        {
            causes |= NetResolutionCauses.Undriven;
        }

        if ((unknownDriverBits[wordIndex] & bitMask) != 0)
        {
            causes |= NetResolutionCauses.UnknownDriver;
        }

        if ((contentionBits[wordIndex] & bitMask) != 0)
        {
            causes |= NetResolutionCauses.Contention;
        }

        return causes;
    }
}
