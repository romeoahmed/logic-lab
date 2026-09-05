using LogicLab.Domain;
using LogicLab.Engine.Compilation;

namespace LogicLab.Engine.Tests;

internal sealed class PackedMemoryTests
{
    [Test]
    [Arguments(-1)]
    [Arguments(int.MinValue)]
    [Arguments(2)]
    [Arguments(int.MaxValue)]
    public async Task Access_OutOfRangeAddress_RejectsWithoutChangingMemory(int address)
    {
        var memory = PackedMemory.FromImage(
            MemoryTestCircuit.Create().CreateMemoryImage(
                "Words", [[LogicValue.Zero], [LogicValue.One]]),
            CancellationToken.None);
        var original = memory.Clone();
        var value = new LogicVector([LogicValue.One]);

        using (Assert.Multiple())
        {
            await Assert.That(() => memory.ReadWord(address))
                .ThrowsExactly<ArgumentOutOfRangeException>();
            await Assert.That(() => memory.ReadMerged([address], CancellationToken.None))
                .ThrowsExactly<ArgumentOutOfRangeException>();
            await Assert.That(() => memory.WordEquals(address, value))
                .ThrowsExactly<ArgumentOutOfRangeException>();
            await Assert.That(() => memory.WriteWord(address, value))
                .ThrowsExactly<ArgumentOutOfRangeException>();
            await Assert.That(memory.ContentEquals(original, CancellationToken.None)).IsTrue();
        }
    }
}
