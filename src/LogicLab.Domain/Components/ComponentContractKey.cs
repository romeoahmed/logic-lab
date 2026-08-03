namespace LogicLab.Domain.Components;

public readonly record struct ComponentContractKey
{
    public ComponentContractKey(string libraryId, string contractId)
    {
        ArgumentException.ThrowIfNullOrEmpty(libraryId);
        ArgumentException.ThrowIfNullOrEmpty(contractId);
        LibraryId = libraryId;
        ContractId = contractId;
    }

    public string LibraryId { get; }

    public string ContractId { get; }
}
