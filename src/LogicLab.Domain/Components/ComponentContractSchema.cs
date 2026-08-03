using System.Collections.ObjectModel;
using LogicLab.Domain.Authoring;

namespace LogicLab.Domain.Components;

public sealed class ComponentContractSchema
{
    internal ComponentContractSchema(
        ComponentContractKey key,
        ComponentParameterSchema[] parameters,
        ComponentPortSchema[] ports)
    {
        Key = key;
        Parameters = Array.AsReadOnly(
            (ComponentParameterSchema[])parameters.Clone());
        Ports = Array.AsReadOnly(
            (ComponentPortSchema[])ports.Clone());
        SchemaDigest = ComponentContractSchemaDigest.Compute(
            Key,
            Parameters,
            Ports);
    }

    public ComponentContractKey Key { get; }

    public ReadOnlyCollection<ComponentParameterSchema> Parameters { get; }

    public ReadOnlyCollection<ComponentPortSchema> Ports { get; }

    public string SchemaDigest { get; }

    public ComponentPortShape ResolvePortShape(
        IReadOnlyList<ComponentParameterBinding> parameters,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var ownedParameters = Array.AsReadOnly(parameters.ToArray());
        if (!TryResolvePortShape(ownedParameters, cancellationToken, out var shape))
        {
            throw InvalidParameters(nameof(parameters));
        }

        return shape;
    }

    public ReadOnlyCollection<ResolvedComponentPortSchema> ResolvePorts(
        IReadOnlyList<ComponentParameterBinding> parameters)
    {
        return ResolvePorts(parameters, CancellationToken.None);
    }

    public ReadOnlyCollection<ResolvedComponentPortSchema> ResolvePorts(
        IReadOnlyList<ComponentParameterBinding> parameters,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var ownedParameters = Array.AsReadOnly(parameters.ToArray());
        if (!TryResolvePortShape(ownedParameters, cancellationToken, out var shape)
            || shape.PortCount > int.MaxValue)
        {
            throw InvalidParameters(nameof(parameters));
        }

        return ComponentPortResolver.Materialize(
            Ports,
            ownedParameters,
            shape,
            cancellationToken);
    }

    internal bool TryResolvePorts(
        ReadOnlyCollection<ComponentParameterBinding> parameters,
        out ReadOnlyCollection<ResolvedComponentPortSchema> ports)
    {
        if (!TryResolvePortShape(
                parameters,
                CancellationToken.None,
                out var shape)
            || shape.PortCount > int.MaxValue)
        {
            ports = Array.AsReadOnly<ResolvedComponentPortSchema>([]);
            return false;
        }

        ports = ComponentPortResolver.Materialize(
            Ports,
            parameters,
            shape,
            CancellationToken.None);
        return true;
    }

    internal bool TryResolvePortShape(
        ReadOnlyCollection<ComponentParameterBinding> parameters,
        CancellationToken cancellationToken,
        out ComponentPortShape shape)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (ComponentParameterValidator.Validate(
                Key,
                this,
                parameters,
                cancellationToken).Length > 0)
        {
            shape = ComponentPortShape.Empty;
            return false;
        }

        shape = ComponentPortResolver.Measure(
            Ports,
            parameters,
            cancellationToken);
        return shape.PortCount > 0;
    }

    private static ArgumentException InvalidParameters(string parameterName)
    {
        return new ArgumentException(
            "The parameters do not satisfy the component contract.",
            parameterName);
    }
}
