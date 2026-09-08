using System.Collections.ObjectModel;

namespace LogicLab.Engine.Compilation;

public sealed record SourceMapEntry(
    int Ordinal,
    CompilationSource Source);

public sealed record EvaluatorInputSourceMapEntry(
    int EvaluatorOrdinal,
    int InputOrdinal,
    CompilationSource Source);

public sealed record StronglyConnectedComponentMemberSourceMapEntry(
    int StronglyConnectedComponentOrdinal,
    int EvaluatorOrdinal,
    CompilationSource Source);

public sealed class SourceMap
{
    private readonly Dictionary<CompilationSource, int> evaluatorOrdinals;
    private readonly Dictionary<CompilationSource, int> driverOrdinals;
    private readonly Dictionary<CompilationSource, int> netOrdinals;

    internal SourceMap(
        SourceMapEntry[] ownedEvaluators,
        EvaluatorInputSourceMapEntry[] ownedEvaluatorInputs,
        SourceMapEntry[] ownedDrivers,
        SourceMapEntry[] ownedNets,
        StronglyConnectedComponentMemberSourceMapEntry[]
            ownedStronglyConnectedComponentMembers,
        SourceMapEntry[] ownedNetAliases)
    {
        Evaluators = Array.AsReadOnly(ownedEvaluators);
        EvaluatorInputs = Array.AsReadOnly(ownedEvaluatorInputs);
        Drivers = Array.AsReadOnly(ownedDrivers);
        Nets = Array.AsReadOnly(ownedNets);
        NetAliases = Array.AsReadOnly(ownedNetAliases);
        StronglyConnectedComponentMembers = Array.AsReadOnly(
            ownedStronglyConnectedComponentMembers);
        evaluatorOrdinals = Index(ownedEvaluators);
        driverOrdinals = Index(ownedDrivers);
        netOrdinals = Index(ownedNets, ownedNetAliases);
    }

    public ReadOnlyCollection<SourceMapEntry> Evaluators { get; }

    public ReadOnlyCollection<EvaluatorInputSourceMapEntry> EvaluatorInputs { get; }

    public ReadOnlyCollection<SourceMapEntry> Drivers { get; }

    public ReadOnlyCollection<SourceMapEntry> Nets { get; }

    public ReadOnlyCollection<SourceMapEntry> NetAliases { get; }

    public ReadOnlyCollection<StronglyConnectedComponentMemberSourceMapEntry>
        StronglyConnectedComponentMembers
    { get; }

    public bool TryGetNetOrdinal(
        CompilationSource source,
        out int ordinal)
    {
        ArgumentNullException.ThrowIfNull(source);
        return netOrdinals.TryGetValue(source, out ordinal);
    }

    public bool TryGetDriverOrdinal(
        CompilationSource source,
        out int ordinal)
    {
        ArgumentNullException.ThrowIfNull(source);
        return driverOrdinals.TryGetValue(source, out ordinal);
    }

    internal bool TryGetEvaluatorOrdinal(
        CompilationSource source,
        out int ordinal)
    {
        ArgumentNullException.ThrowIfNull(source);
        return evaluatorOrdinals.TryGetValue(source, out ordinal);
    }

    private static Dictionary<CompilationSource, int> Index(
        SourceMapEntry[] entries,
        SourceMapEntry[]? aliases = null)
    {
        var index = new Dictionary<CompilationSource, int>(checked(
            entries.Length + (aliases?.Length ?? 0)));
        AddEntries(entries);
        if (aliases is not null)
        {
            AddEntries(aliases);
        }

        return index;

        void AddEntries(SourceMapEntry[] additions)
        {
            foreach (var entry in additions)
            {
                index.TryAdd(entry.Source, entry.Ordinal);
            }
        }
    }
}
