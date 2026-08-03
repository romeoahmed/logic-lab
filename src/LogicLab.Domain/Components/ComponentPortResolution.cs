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
        ExceedsPortCountRange = portMeasure.ExceedsUInt64;
    }

    public ulong PortCount
    {
        get
        {
            EnsureRepresentable();
            return portMeasure.Count;
        }
    }

    public bool ExceedsPortCountRange { get; }

    public ReadOnlyCollection<ResolvedComponentPortSchema> Materialize(
        ulong maximumPortCount,
        CancellationToken cancellationToken = default)
    {
        if (!TryMaterialize(maximumPortCount, out var ports, cancellationToken))
        {
            throw new InvalidOperationException(
                "The generated component Port count exceeds the active materialization budget.");
        }

        return ports;
    }

    public bool TryMaterialize(
        ulong maximumPortCount,
        out ReadOnlyCollection<ResolvedComponentPortSchema> ports,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfZero(maximumPortCount);
        cancellationToken.ThrowIfCancellationRequested();
        if (ExceedsPortCountRange
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

    private void EnsureRepresentable()
    {
        if (ExceedsPortCountRange)
        {
            throw new OverflowException(
                "The generated component Port count exceeds the supported unsigned range.");
        }
    }
}
