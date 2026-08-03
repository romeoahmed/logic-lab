namespace LogicLab.Engine.Tests;

internal static class ReadOnlyCollectionAssertions
{
    internal static async Task RejectsMutation<T>(IReadOnlyList<T> values)
    {
        if (values is ICollection<T> collection)
        {
            await Assert.That(collection.IsReadOnly).IsTrue();
        }

        if (values.Count > 0 && values is IList<T> list)
        {
            await Assert.That(() => list[0] = values[0])
                .ThrowsExactly<NotSupportedException>();
        }
    }
}
