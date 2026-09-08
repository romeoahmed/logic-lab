using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using LogicLab.Domain.Authoring;

namespace LogicLab.Domain.Components;

/// <summary>
/// A validated symbolic Port shape that supports lookup without expanding generated families.
/// </summary>
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

    /// <summary>Returns false when the exact Port count exceeds <see cref="ulong.MaxValue"/>.</summary>
    public bool TryGetPortCount(out ulong portCount)
    {
        portCount = portMeasure.Count;
        return !portMeasure.ExceedsUInt64;
    }

    public bool TryResolvePort(
        string portId,
        [NotNullWhen(true)] out ResolvedComponentPortSchema? port,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(portId);
        cancellationToken.ThrowIfCancellationRequested();
        return ComponentPortResolver.TryResolvePort(schemas, parameters, portId, out port, cancellationToken);
    }

    internal bool HasSameShape(ComponentPortResolution other) =>
        schemas.SequenceEqual(other.schemas)
        && ComponentPortResolver.HaveSameShape(schemas, parameters, other.parameters);

    /// <summary>
    /// Expands Ports in contract order only when their count fits both the supplied budget
    /// and a managed collection; otherwise returns false with an empty collection.
    /// </summary>
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
