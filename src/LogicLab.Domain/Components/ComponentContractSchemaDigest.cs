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
        ReadOnlyCollection<ComponentPortSchema> ports,
        string stateShapeId,
        string semanticRuleVersion)
    {
        var canonical = new StringBuilder();
        canonical.Append("componentContractSchemaV2\u001f")
            .Append(key.LibraryId).Append('\u001f')
            .Append(key.ContractId).Append('\n');
        canonical.Append("stateShape\u001f")
            .Append(stateShapeId)
            .Append('\n');
        canonical.Append("semanticRuleVersion\u001f")
            .Append(semanticRuleVersion)
            .Append('\n');
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
            // Schema V2 uses fixed columns; fields owned by other kinds are empty.
            var widthParameterId = parameter switch
            {
                VariableLogicVectorParameterSchema vector => vector.WidthParameterId,
                SlicesParameterSchema slices => slices.WidthParameterId,
                _ => string.Empty,
            };
            var minimumItemCount = parameter switch
            {
                SlicesParameterSchema slices => slices.MinimumItemCount,
                WidthsParameterSchema widths => widths.MinimumItemCount,
                _ => 0,
            };
            var greaterThanParameterId = (parameter as WidthParameterSchema)?.GreaterThanParameterId;
            ReadOnlyCollection<string> allowedValues = parameter is ChoiceParameterSchema choice
                ? choice.AllowedValues : [];
            canonical.Append("parameter\u001f")
                .Append(parameter.Id).Append('\u001f')
                .Append(ParameterKindToken(parameter.Kind)).Append('\u001f')
                .Append(widthParameterId).Append('\u001f')
                .Append(minimumItemCount.ToString(CultureInfo.InvariantCulture))
                .Append('\u001f')
                .Append(greaterThanParameterId ?? string.Empty).Append('\u001f')
                .AppendJoin('\u001e', allowedValues)
                .Append('\n');
            if (parameter is WidthParameterSchema { MinimumValue: > 1 } width)
            {
                canonical.Append("minimumValue\u001f")
                    .Append(parameter.Id).Append('\u001f')
                    .Append(width.MinimumValue.ToString(CultureInfo.InvariantCulture))
                    .Append('\n');
            }

            if (parameter is MemoryImageParameterSchema image)
            {
                canonical.Append("memoryImageShape\u001f")
                    .Append(parameter.Id).Append('\u001f')
                    .Append(image.WordWidthParameterId).Append('\u001f')
                    .Append(image.AddressWidthParameterId)
                    .Append('\n');
            }

            if (parameter is FixedLogicVectorParameterSchema fixedWidth)
            {
                canonical.Append("fixedWidth\u001f")
                    .Append(parameter.Id).Append('\u001f')
                    .Append(fixedWidth.Width.ToString(CultureInfo.InvariantCulture))
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
            ComponentParameterKind.BinaryLogicValue => "binaryLogicValue",
            ComponentParameterKind.PositiveUnsigned64 => "positiveUnsigned64",
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
