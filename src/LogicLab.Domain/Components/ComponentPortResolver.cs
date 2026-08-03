using System.Collections.ObjectModel;
using System.Globalization;
using System.Numerics;
using LogicLab.Domain.Authoring;

namespace LogicLab.Domain.Components;

internal static class ComponentPortResolver
{
    public static ComponentPortMeasure Measure(
        ReadOnlyCollection<ComponentPortSchema> ports,
        ReadOnlyCollection<ComponentParameterBinding> parameters,
        CancellationToken cancellationToken)
    {
        ulong portCount = 0;
        foreach (var port in ports)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var portMeasure = ResolvePortCount(port, parameters);
            if (portMeasure.ExceedsUInt64
                || ulong.MaxValue - portCount < portMeasure.Count)
            {
                return new ComponentPortMeasure(ulong.MaxValue, ExceedsUInt64: true);
            }

            portCount += portMeasure.Count;
        }

        return new ComponentPortMeasure(portCount, ExceedsUInt64: false);
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

    public static bool TryResolvePort(
        ReadOnlyCollection<ComponentPortSchema> ports,
        ReadOnlyCollection<ComponentParameterBinding> parameters,
        string portId,
        out ResolvedComponentPortSchema? resolved,
        CancellationToken cancellationToken)
    {
        foreach (var port in ports)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryGetPortIndex(port, parameters, portId, out var index))
            {
                continue;
            }

            resolved = new ResolvedComponentPortSchema(
                portId,
                port.Direction,
                ResolvePortWidth(port, parameters, index, cancellationToken));
            return true;
        }

        resolved = null;
        return false;
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
                    CeilingLog2(count)));
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
                resolved.Add(new ResolvedComponentPortSchema(
                    port.Id,
                    port.Direction,
                    SumWidths(summedWidths, cancellationToken)));
                return;
            default:
                throw new InvalidOperationException(
                    "The component Port width source is undefined.");
        }
    }

    private static ComponentPortMeasure ResolvePortCount(
        ComponentPortSchema port,
        IEnumerable<ComponentParameterBinding> parameters)
    {
        if (port.Cardinality == ComponentPortCardinality.PowerOfTwoParameterValue)
        {
            var exponent = GetValue<Unsigned32ParameterValue>(
                parameters,
                port.CardinalityParameterId!).Value;
            return exponent >= 64
                ? new ComponentPortMeasure(ulong.MaxValue, ExceedsUInt64: true)
                : new ComponentPortMeasure(1UL << checked((int)exponent), ExceedsUInt64: false);
        }

        ulong count = port.Cardinality switch
        {
            ComponentPortCardinality.Fixed => 1UL,
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
            _ => throw new InvalidOperationException(
                "The component Port cardinality is undefined."),
        };
        return new ComponentPortMeasure(count, ExceedsUInt64: false);
    }

    private static bool TryGetPortIndex(
        ComponentPortSchema port,
        ReadOnlyCollection<ComponentParameterBinding> parameters,
        string portId,
        out ulong index)
    {
        index = 0;
        if (port.Cardinality == ComponentPortCardinality.Fixed)
        {
            return string.Equals(port.Id, portId, StringComparison.Ordinal);
        }

        if (!portId.StartsWith(port.Id, StringComparison.Ordinal))
        {
            return false;
        }

        var suffix = portId.AsSpan(port.Id.Length);
        if (suffix.IsEmpty
            || (suffix.Length > 1 && suffix[0] == '0')
            || !ulong.TryParse(
                suffix,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out index))
        {
            return false;
        }

        var measure = ResolvePortCount(port, parameters);
        return measure.ExceedsUInt64 || index < measure.Count;
    }

    private static uint ResolvePortWidth(
        ComponentPortSchema port,
        ReadOnlyCollection<ComponentParameterBinding> parameters,
        ulong index,
        CancellationToken cancellationToken)
    {
        return port.WidthSource switch
        {
            ComponentPortWidthSource.ParameterValue =>
                GetValue<Unsigned32ParameterValue>(parameters, port.ParameterId).Value,
            ComponentPortWidthSource.FixedOne => 1,
            ComponentPortWidthSource.CeilingLog2ParameterValue => CeilingLog2(
                GetValue<Unsigned32ParameterValue>(parameters, port.ParameterId).Value),
            ComponentPortWidthSource.SliceLength => GetValue<SlicesParameterValue>(
                parameters,
                port.ParameterId).Values[checked((int)index)].Length,
            ComponentPortWidthSource.WidthItem => GetValue<WidthsParameterValue>(
                parameters,
                port.ParameterId).Values[checked((int)index)],
            ComponentPortWidthSource.WidthSum => SumWidths(
                GetValue<WidthsParameterValue>(parameters, port.ParameterId),
                cancellationToken),
            _ => throw new InvalidOperationException(
                "The component Port width source is undefined."),
        };
    }

    private static uint CeilingLog2(uint count)
    {
        return Math.Max(1U, checked((uint)BitOperations.Log2(count - 1) + 1U));
    }

    private static uint SumWidths(
        WidthsParameterValue widths,
        CancellationToken cancellationToken)
    {
        uint sum = 0;
        foreach (var width in widths.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            sum = checked(sum + width);
        }

        return sum;
    }

    private static void AppendUniformPorts(
        List<ResolvedComponentPortSchema> resolved,
        ComponentPortSchema port,
        ReadOnlyCollection<ComponentParameterBinding> parameters,
        uint width,
        CancellationToken cancellationToken)
    {
        var measure = ResolvePortCount(port, parameters);
        if (measure.ExceedsUInt64)
        {
            throw new OverflowException(
                "The generated component Port count exceeds the supported unsigned range.");
        }

        var count = measure.Count;
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

internal readonly record struct ComponentPortMeasure(ulong Count, bool ExceedsUInt64);
