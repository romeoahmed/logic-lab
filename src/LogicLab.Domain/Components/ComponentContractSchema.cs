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

    public ComponentPortResolution PreparePorts(
        IReadOnlyList<ComponentParameterBinding> parameters,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var ownedParameters = Array.AsReadOnly(parameters.ToArray());
        if (!TryPreparePorts(
                ownedParameters,
                cancellationToken,
                out var resolution))
        {
            throw InvalidParameters(nameof(parameters));
        }

        return resolution;
    }

    internal bool TryResolvePorts(
        ReadOnlyCollection<ComponentParameterBinding> parameters,
        out ReadOnlyCollection<ResolvedComponentPortSchema> ports)
    {
        if (!TryPreparePorts(
                parameters,
                CancellationToken.None,
                out var resolution))
        {
            ports = Array.AsReadOnly<ResolvedComponentPortSchema>([]);
            return false;
        }

        ports = resolution.Materialize();
        return true;
    }

    private bool TryPreparePorts(
        ReadOnlyCollection<ComponentParameterBinding> parameters,
        CancellationToken cancellationToken,
        out ComponentPortResolution resolution)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (ComponentParameterValidator.Validate(
                Key,
                this,
                parameters,
                cancellationToken).Length > 0)
        {
            resolution = null!;
            return false;
        }

        var portCount = ComponentPortResolver.Measure(
            Ports,
            parameters,
            cancellationToken);
        resolution = new ComponentPortResolution(Ports, parameters, portCount);
        return portCount > 0;
    }

    private static ArgumentException InvalidParameters(string parameterName)
    {
        return new ArgumentException(
            "The parameters do not satisfy the component contract.",
            parameterName);
    }
}
