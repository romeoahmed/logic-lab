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
        CancellationToken cancellationToken = default)
    {
        EnsureRepresentable();

        return ComponentPortResolver.Materialize(
            schemas,
            parameters,
            PortCount,
            cancellationToken);
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
