using System.Collections.ObjectModel;
using LogicLab.Domain.Authoring;

namespace LogicLab.Domain.Components;

public sealed class ComponentPortResolution
{
    private readonly ReadOnlyCollection<ComponentPortSchema> schemas;
    private readonly ReadOnlyCollection<ComponentParameterBinding> parameters;

    internal ComponentPortResolution(
        ReadOnlyCollection<ComponentPortSchema> schemas,
        ReadOnlyCollection<ComponentParameterBinding> parameters,
        ulong portCount)
    {
        this.schemas = schemas;
        this.parameters = parameters;
        PortCount = portCount;
    }

    public ulong PortCount { get; }

    public ReadOnlyCollection<ResolvedComponentPortSchema> Materialize(
        CancellationToken cancellationToken = default)
    {
        return ComponentPortResolver.Materialize(
            schemas,
            parameters,
            PortCount,
            cancellationToken);
    }
}
