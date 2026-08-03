using System.Collections.ObjectModel;
using LogicLab.Domain.Authoring;

namespace LogicLab.Domain.Components;

internal enum ComponentPortResolutionKind
{
    Fixed,
    Split,
    Concat,
}

public sealed class ComponentContractSchema
{
    internal ComponentContractSchema(
        ComponentContractKey key,
        ComponentParameterSchema[] parameters,
        ComponentPortSchema[] ports,
        ComponentPortResolutionKind portResolutionKind =
            ComponentPortResolutionKind.Fixed)
    {
        Key = key;
        Parameters = Array.AsReadOnly(
            (ComponentParameterSchema[])parameters.Clone());
        Ports = Array.AsReadOnly(
            (ComponentPortSchema[])ports.Clone());
        PortResolutionKind = portResolutionKind;
    }

    public ComponentContractKey Key { get; }

    public ReadOnlyCollection<ComponentParameterSchema> Parameters { get; }

    public ReadOnlyCollection<ComponentPortSchema> Ports { get; }

    internal ComponentPortResolutionKind PortResolutionKind { get; }

    public ReadOnlyCollection<ResolvedComponentPortSchema> ResolvePorts(
        IReadOnlyList<ComponentParameterBinding> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var ownedParameters = Array.AsReadOnly(parameters.ToArray());
        if (ComponentParameterValidator.Validate(Key, this, ownedParameters).Length > 0
            || !TryResolvePorts(ownedParameters, out var ports))
        {
            throw new ArgumentException(
                "The parameters do not satisfy the component contract.",
                nameof(parameters));
        }

        return ports;
    }

    internal bool TryResolvePorts(
        ReadOnlyCollection<ComponentParameterBinding> parameters,
        out ReadOnlyCollection<ResolvedComponentPortSchema> ports)
    {
        var resolved = new List<ResolvedComponentPortSchema>();
        try
        {
            switch (PortResolutionKind)
            {
                case ComponentPortResolutionKind.Fixed:
                    foreach (var port in Ports)
                    {
                        if (!TryGetWidth(parameters, port.WidthParameterId, out var width))
                        {
                            ports = Array.AsReadOnly<ResolvedComponentPortSchema>([]);
                            return false;
                        }

                        resolved.Add(new ResolvedComponentPortSchema(
                            port.Id,
                            port.Direction,
                            width));
                    }

                    break;
                case ComponentPortResolutionKind.Split:
                    if (!TryGetWidth(parameters, "width", out var inputWidth)
                        || TryGetValue<SlicesParameterValue>(parameters, "slices")
                            is not { } slices)
                    {
                        ports = Array.AsReadOnly<ResolvedComponentPortSchema>([]);
                        return false;
                    }

                    resolved.Add(new ResolvedComponentPortSchema(
                        "D",
                        PortDirection.Input,
                        inputWidth));
                    for (var index = 0; index < slices.Values.Count; index++)
                    {
                        resolved.Add(new ResolvedComponentPortSchema(
                            $"Q{index}",
                            PortDirection.Output,
                            slices.Values[index].Length));
                    }

                    break;
                case ComponentPortResolutionKind.Concat:
                    if (TryGetValue<WidthsParameterValue>(parameters, "inputWidths")
                        is not { } widths)
                    {
                        ports = Array.AsReadOnly<ResolvedComponentPortSchema>([]);
                        return false;
                    }

                    uint outputWidth = 0;
                    for (var index = 0; index < widths.Values.Count; index++)
                    {
                        var currentInputWidth = widths.Values[index];
                        resolved.Add(new ResolvedComponentPortSchema(
                            $"D{index}",
                            PortDirection.Input,
                            currentInputWidth));
                        outputWidth = checked(outputWidth + currentInputWidth);
                    }

                    resolved.Add(new ResolvedComponentPortSchema(
                        "Q",
                        PortDirection.Output,
                        outputWidth));
                    break;
                default:
                    throw new InvalidOperationException(
                        "The component port resolution kind is undefined.");
            }
        }
        catch (OverflowException)
        {
            ports = Array.AsReadOnly<ResolvedComponentPortSchema>([]);
            return false;
        }

        ports = Array.AsReadOnly(resolved.ToArray());
        return resolved.All(port => port.Width > 0);
    }

    private static bool TryGetWidth(
        IEnumerable<ComponentParameterBinding> parameters,
        string parameterId,
        out uint width)
    {
        var value = TryGetValue<Unsigned32ParameterValue>(parameters, parameterId);
        width = value?.Value ?? 0;
        return width > 0;
    }

    private static T? TryGetValue<T>(
        IEnumerable<ComponentParameterBinding> parameters,
        string parameterId)
        where T : ComponentParameterValue
    {
        return parameters
            .SingleOrDefault(binding => string.Equals(
                binding.ParameterId,
                parameterId,
                StringComparison.Ordinal))
            ?.Value as T;
    }
}
