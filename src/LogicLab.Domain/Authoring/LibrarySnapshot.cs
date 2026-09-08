using System.Collections.Frozen;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using LogicLab.Domain.Components;

namespace LogicLab.Domain.Authoring;

public sealed class LibrarySnapshot
{
    private readonly FrozenDictionary<string, ComponentContractSchema> contractsById;

    private LibrarySnapshot(
        string libraryId,
        string version,
        string contentDigest,
        ReadOnlyCollection<ComponentContractSchema> contracts)
    {
        LibraryId = libraryId;
        Version = version;
        ContentDigest = contentDigest;
        Contracts = contracts;
        contractsById = contracts.ToFrozenDictionary(contract => contract.Key.ContractId, StringComparer.Ordinal);
        Fingerprint = Convert.ToHexStringLower(SHA256.HashData(
            Encoding.UTF8.GetBytes($"{libraryId}\n{version}\n{contentDigest}\n")));
    }

    public static LibrarySnapshot Core { get; } = new(
        CoreLibrarySchema.LibraryId,
        CoreLibrarySchema.Version,
        CoreLibrarySchema.ContentDigest,
        CoreLibrarySchema.Contracts);

    public string LibraryId { get; }

    public string Version { get; }

    public string ContentDigest { get; }

    public string Fingerprint { get; }

    public ReadOnlyCollection<ComponentContractSchema> Contracts { get; }

    public ComponentContractSchema? ResolveContract(ComponentContractKey key)
    {
        return string.Equals(key.LibraryId, LibraryId, StringComparison.Ordinal)
            ? contractsById.GetValueOrDefault(key.ContractId)
            : null;
    }
}
