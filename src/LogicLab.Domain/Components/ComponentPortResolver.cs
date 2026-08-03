using System.Collections.ObjectModel;
using System.Globalization;
using System.Numerics;
using LogicLab.Domain.Authoring;

namespace LogicLab.Domain.Components;

internal static class ComponentPortResolver
{
    public static ulong Measure(
        ReadOnlyCollection<ComponentPortSchema> ports,
        ReadOnlyCollection<ComponentParameterBinding> parameters,
        CancellationToken cancellationToken)
    {
        ulong portCount = 0;
        foreach (var port in ports)
        {
            cancellationToken.ThrowIfCancellationRequested();
            portCount = checked(portCount + ResolvePortCount(port, parameters));
        }

        return portCount;
    }

    public static ReadOnlyCollection<ResolvedComponentPortSchema> Materialize(
        ReadOnlyCollection<ComponentPortSchema> ports,
        ReadOnlyCollection<ComponentParameterBinding> parameters,
        ulong portCount,
        CancellationToken cancellationToken)
    {
        var resolved = new List<ResolvedComponentPortSchema>(
            checked((int)portCount));
        foreach (var port in ports)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AppendPorts(resolved, port, parameters, cancellationToken);
        }

        return Array.AsReadOnly(resolved.ToArray());
    }

    private static void AppendPorts(
        List<ResolvedComponentPortSchema> resolved,
        ComponentPortSchema port,
        ReadOnlyCollection<ComponentParameterBinding> parameters,
        CancellationToken cancellationToken)
    {
        switch (port.WidthSource)
        {
            case ComponentPortWidthSource.ParameterValue:
                AppendUniformPorts(
                    resolved,
                    port,
                    parameters,
                    GetValue<Unsigned32ParameterValue>(
                        parameters,
                        port.ParameterId).Value,
                    cancellationToken);
                return;
            case ComponentPortWidthSource.FixedOne:
                AppendUniformPorts(resolved, port, parameters, 1, cancellationToken);
                return;
            case ComponentPortWidthSource.CeilingLog2ParameterValue:
                var count = GetValue<Unsigned32ParameterValue>(
                    parameters,
                    port.ParameterId).Value;
                resolved.Add(new ResolvedComponentPortSchema(
                    port.Id,
                    port.Direction,
                    Math.Max(1U, checked((uint)BitOperations.Log2(count - 1) + 1U))));
                return;
            case ComponentPortWidthSource.SliceLength:
                var slices = GetValue<SlicesParameterValue>(
                    parameters,
                    port.ParameterId);
                for (var index = 0; index < slices.Values.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    resolved.Add(new ResolvedComponentPortSchema(
                        IndexedPortId(port, index),
                        port.Direction,
                        slices.Values[index].Length));
                }

                return;
            case ComponentPortWidthSource.WidthItem:
                var widths = GetValue<WidthsParameterValue>(
                    parameters,
                    port.ParameterId);
                for (var index = 0; index < widths.Values.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    resolved.Add(new ResolvedComponentPortSchema(
                        IndexedPortId(port, index),
                        port.Direction,
                        widths.Values[index]));
                }

                return;
            case ComponentPortWidthSource.WidthSum:
                var summedWidths = GetValue<WidthsParameterValue>(
                    parameters,
                    port.ParameterId);
                uint sum = 0;
                foreach (var width in summedWidths.Values)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    sum = checked(sum + width);
                }

                resolved.Add(new ResolvedComponentPortSchema(
                    port.Id,
                    port.Direction,
                    sum));
                return;
            default:
                throw new InvalidOperationException(
                    "The component Port width source is undefined.");
        }
    }

    private static ulong ResolvePortCount(
        ComponentPortSchema port,
        IEnumerable<ComponentParameterBinding> parameters)
    {
        return port.Cardinality switch
        {
            ComponentPortCardinality.Fixed => 1,
            ComponentPortCardinality.ParameterItems when
                port.WidthSource == ComponentPortWidthSource.SliceLength =>
                checked((ulong)GetValue<SlicesParameterValue>(
                    parameters,
                    port.ParameterId).Values.Count),
            ComponentPortCardinality.ParameterItems when
                port.WidthSource == ComponentPortWidthSource.WidthItem =>
                checked((ulong)GetValue<WidthsParameterValue>(
                    parameters,
                    port.ParameterId).Values.Count),
            ComponentPortCardinality.ParameterValue =>
                GetValue<Unsigned32ParameterValue>(
                    parameters,
                    port.CardinalityParameterId!).Value,
            ComponentPortCardinality.PowerOfTwoParameterValue =>
                checked(1UL << checked((int)GetValue<Unsigned32ParameterValue>(
                    parameters,
                    port.CardinalityParameterId!).Value)),
            _ => throw new InvalidOperationException(
                "The component Port cardinality is undefined."),
        };
    }

    private static void AppendUniformPorts(
        List<ResolvedComponentPortSchema> resolved,
        ComponentPortSchema port,
        ReadOnlyCollection<ComponentParameterBinding> parameters,
        uint width,
        CancellationToken cancellationToken)
    {
        var count = ResolvePortCount(port, parameters);
        for (ulong index = 0; index < count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            resolved.Add(new ResolvedComponentPortSchema(
                port.Cardinality == ComponentPortCardinality.Fixed
                    ? port.Id
                    : IndexedPortId(port, checked((int)index)),
                port.Direction,
                width));
        }
    }

    private static string IndexedPortId(ComponentPortSchema port, int index)
    {
        return port.Indexing switch
        {
            ComponentPortIndexing.ZeroBasedDecimal => string.Concat(
                port.Id,
                index.ToString(CultureInfo.InvariantCulture)),
            _ => throw new InvalidOperationException(
                "The component Port indexing is undefined."),
        };
    }

    private static T GetValue<T>(
        IEnumerable<ComponentParameterBinding> parameters,
        string parameterId)
        where T : ComponentParameterValue
    {
        return parameters
            .Single(binding => string.Equals(
                binding.ParameterId,
                parameterId,
                StringComparison.Ordinal))
            .Value as T
            ?? throw new InvalidOperationException(
                "A validated component parameter has an unexpected value kind.");
    }
}
