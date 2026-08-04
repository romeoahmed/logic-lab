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

    public ComponentPortResolution ResolvePorts(
        IReadOnlyList<ComponentParameterBinding> parameters,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var ownedParameters = Array.AsReadOnly(parameters.ToArray());
        cancellationToken.ThrowIfCancellationRequested();
        if (ComponentParameterValidator.Validate(
                Key,
                this,
                ownedParameters,
                cancellationToken: cancellationToken).Length > 0)
        {
            throw InvalidParameters(nameof(parameters));
        }

        var portMeasure = ComponentPortResolver.Measure(
            Ports,
            ownedParameters,
            cancellationToken);
        if (portMeasure.Count == 0)
        {
            throw new InvalidOperationException(
                "A component contract must resolve at least one Port.");
        }

        return new ComponentPortResolution(Ports, ownedParameters, portMeasure);
    }

    public bool TryResolvePort(
        IReadOnlyList<ComponentParameterBinding> parameters,
        string portId,
        out ResolvedComponentPortSchema? port,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentException.ThrowIfNullOrEmpty(portId);
        var ownedParameters = Array.AsReadOnly(parameters.ToArray());
        if (ComponentParameterValidator.Validate(
                Key,
                this,
                ownedParameters,
                cancellationToken: cancellationToken).Length > 0)
        {
            port = null;
            return false;
        }

        return ComponentPortResolver.TryResolvePort(
            Ports,
            ownedParameters,
            portId,
            out port,
            cancellationToken);
    }

    private static ArgumentException InvalidParameters(string parameterName)
    {
        return new ArgumentException(
            "The parameters do not satisfy the component contract.",
            parameterName);
    }
}
