using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace LogicLab.Domain.Components;

internal static class ComponentContractSchemaDigest
{
    public static string Compute(
        ComponentContractKey key,
        ReadOnlyCollection<ComponentParameterSchema> parameters,
        ReadOnlyCollection<ComponentPortSchema> ports)
    {
        var canonical = new StringBuilder();
        canonical.Append("componentContractSchemaV1\u001f")
            .Append(key.LibraryId).Append('\u001f')
            .Append(key.ContractId).Append('\n');
        AppendParameters(canonical, parameters);
        AppendPorts(canonical, ports);

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()));
        return Convert.ToHexStringLower(digest);
    }

    private static void AppendParameters(
        StringBuilder canonical,
        ReadOnlyCollection<ComponentParameterSchema> parameters)
    {
        foreach (var parameter in parameters)
        {
            canonical.Append("parameter\u001f")
                .Append(parameter.Id).Append('\u001f')
                .Append(ParameterKindToken(parameter.Kind)).Append('\u001f')
                .Append(parameter.WidthParameterId ?? string.Empty).Append('\u001f')
                .Append(parameter.MinimumItemCount.ToString(CultureInfo.InvariantCulture))
                .Append('\u001f')
                .Append(parameter.GreaterThanParameterId ?? string.Empty).Append('\u001f')
                .AppendJoin('\u001e', parameter.AllowedValues)
                .Append('\n');
            if (parameter.MinimumValue > 1)
            {
                canonical.Append("minimumValue\u001f")
                    .Append(parameter.Id).Append('\u001f')
                    .Append(parameter.MinimumValue.ToString(CultureInfo.InvariantCulture))
                    .Append('\n');
            }
        }
    }

    private static void AppendPorts(
        StringBuilder canonical,
        ReadOnlyCollection<ComponentPortSchema> ports)
    {
        foreach (var port in ports)
        {
            canonical.Append("portTemplate\u001f")
                .Append(port.Id).Append('\u001f')
                .Append(DirectionToken(port.Direction)).Append('\u001f')
                .Append(CardinalityToken(port.Cardinality)).Append('\u001f')
                .Append(IndexingToken(port.Indexing)).Append('\u001f')
                .Append(WidthSourceToken(port.WidthSource)).Append('\u001f')
                .Append(port.ParameterId)
                .Append('\n');
            if (port.CardinalityParameterId is not null)
            {
                canonical.Append("cardinalityParameter\u001f")
                    .Append(port.Id).Append('\u001f')
                    .Append(port.CardinalityParameterId)
                    .Append('\n');
            }
        }
    }

    private static string ParameterKindToken(ComponentParameterKind kind)
    {
        return kind switch
        {
            ComponentParameterKind.PositiveWidth => "positiveWidth",
            ComponentParameterKind.LogicVector => "logicVector",
            ComponentParameterKind.Choice => "choice",
            ComponentParameterKind.Slices => "slices",
            ComponentParameterKind.Widths => "widths",
            ComponentParameterKind.MemoryImage => "memoryImage",
            _ => throw new InvalidOperationException(
                "The component parameter kind is undefined."),
        };
    }

    private static string DirectionToken(PortDirection direction)
    {
        return direction switch
        {
            PortDirection.Input => "input",
            PortDirection.Output => "output",
            _ => throw new InvalidOperationException(
                "The Port direction is undefined."),
        };
    }

    private static string CardinalityToken(ComponentPortCardinality cardinality)
    {
        return cardinality switch
        {
            ComponentPortCardinality.Fixed => "fixed",
            ComponentPortCardinality.ParameterItems => "parameterItems",
            ComponentPortCardinality.ParameterValue => "parameterValue",
            ComponentPortCardinality.PowerOfTwoParameterValue => "powerOfTwoParameterValue",
            _ => throw new InvalidOperationException(
                "The component Port cardinality is undefined."),
        };
    }

    private static string WidthSourceToken(ComponentPortWidthSource widthSource)
    {
        return widthSource switch
        {
            ComponentPortWidthSource.ParameterValue => "parameterValue",
            ComponentPortWidthSource.SliceLength => "sliceLength",
            ComponentPortWidthSource.WidthItem => "widthItem",
            ComponentPortWidthSource.WidthSum => "widthSum",
            ComponentPortWidthSource.FixedOne => "fixedOne",
            ComponentPortWidthSource.CeilingLog2ParameterValue => "ceilingLog2ParameterValue",
            _ => throw new InvalidOperationException(
                "The component Port width source is undefined."),
        };
    }

    private static string IndexingToken(ComponentPortIndexing indexing)
    {
        return indexing switch
        {
            ComponentPortIndexing.None => "none",
            ComponentPortIndexing.ZeroBasedDecimal => "zeroBasedDecimal",
            _ => throw new InvalidOperationException(
                "The component Port indexing is undefined."),
        };
    }
}
