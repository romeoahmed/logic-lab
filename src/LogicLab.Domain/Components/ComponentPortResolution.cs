using System.Collections.ObjectModel;
using LogicLab.Domain.Authoring;

namespace LogicLab.Domain.Components;

public sealed class ComponentPortResolution
{
    private readonly ReadOnlyCollection<ComponentPortSchema> schemas;
    private readonly ReadOnlyCollection<ComponentParameterBinding> parameters;
    private readonly ComponentPortMeasure portMeasure;

    internal ComponentPortResolution(
        ReadOnlyCollection<ComponentPortSchema> schemas,
        ReadOnlyCollection<ComponentParameterBinding> parameters,
        ComponentPortMeasure portMeasure)
    {
        this.schemas = schemas;
        this.parameters = parameters;
        this.portMeasure = portMeasure;
    }

    public bool TryGetPortCount(out ulong portCount)
    {
        portCount = portMeasure.Count;
        return !portMeasure.ExceedsUInt64;
    }

    public bool TryMaterialize(
        ulong maximumPortCount,
        out ReadOnlyCollection<ResolvedComponentPortSchema> ports,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfZero(maximumPortCount);
        cancellationToken.ThrowIfCancellationRequested();
        if (portMeasure.ExceedsUInt64
            || portMeasure.Count > maximumPortCount
            || portMeasure.Count > int.MaxValue)
        {
            ports = Array.AsReadOnly<ResolvedComponentPortSchema>([]);
            return false;
        }

        ports = ComponentPortResolver.Materialize(
            schemas,
            parameters,
            portMeasure.Count,
            cancellationToken);
        return true;
    }
}
