using System.Collections;

namespace LogicLab.Engine.Tests;

internal sealed class ChangingReadOnlyList<T>(params IReadOnlyList<T>[] snapshots) : IReadOnlyList<T>
{
    private readonly int? initialReportedCount;
    private readonly IReadOnlyList<T>[] snapshots = snapshots;
    private int countReadCount;
    private int enumerationCount;

    public ChangingReadOnlyList(
        int initialReportedCount,
        IReadOnlyList<T> snapshot)
        : this(snapshot)
    {
        this.initialReportedCount = initialReportedCount;
    }

    public int Count => initialReportedCount is int count && countReadCount++ == 0
        ? count
        : snapshots[0].Count;

    public T this[int index] => snapshots[0][index];

    public IEnumerator<T> GetEnumerator()
    {
        var snapshotIndex = Math.Min(enumerationCount++, snapshots.Length - 1);
        return snapshots[snapshotIndex].GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}
