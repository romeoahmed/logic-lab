using System.Globalization;
using System.Runtime.ExceptionServices;
using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;

namespace LogicLab.ProjectFormat;

internal static class CanonicalProjectJson
{
    public static ulong Measure(
        ProjectDocument document,
        ulong[] observations,
        PackagePolicy policy,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return CanonicalJson.Measure(
            writer => WriteDocument(writer, document),
            observations,
            policy,
            cancellationToken);
    }

    public static byte[] Write(
        ProjectDocument document,
        ulong measuredByteCount,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return CanonicalJson.Write(
            writer => WriteDocument(writer, document),
            measuredByteCount,
            cancellationToken);
    }

    private static void WriteDocument(
        CanonicalJsonWriter writer,
        ProjectDocument document)
    {
        writer.WriteStartObject();
        writer.WriteString("projectId", document.ProjectId.Value);
        writer.WriteString("displayName", document.DisplayName);
        writer.WritePropertyName("symbolProfile");
        writer.WriteStartObject();
        writer.WriteString("id", document.SymbolProfile.Id);
        writer.WriteString("version", document.SymbolProfile.Version);
        writer.WriteString(
            "indicationConvention",
            document.SymbolProfile.IndicationConvention switch
            {
                IndicationConvention.Negation => "negation",
                IndicationConvention.DirectPolarity => "directPolarity",
                _ => throw new InvalidOperationException("Unknown indication convention."),
            });
        writer.WriteEndObject();
        writer.WritePropertyName("libraryReferences");
        writer.WriteStartArray();
        writer.WriteStartObject();
        writer.WriteString("id", document.LibrarySnapshot.LibraryId);
        writer.WriteString("version", document.LibrarySnapshot.Version);
        writer.WriteString("digest", document.LibrarySnapshot.ContentDigest);
        writer.WriteEndObject();
        writer.WriteEndArray();
        writer.WriteString(
            "entryCircuitDefinitionId",
            document.EntryCircuitDefinitionId.Value);
        writer.WritePropertyName("circuitDefinitions");
        writer.WriteStartArray();
        foreach (var definition in OrderById(
                     document.CircuitDefinitions,
                     static item => item.Id.Value,
                     writer.CancellationToken))
        {
            WriteDefinition(writer, definition);
        }

        writer.WriteEndArray();
        writer.WritePropertyName("memoryImages");
        writer.WriteStartArray();
        foreach (var image in OrderById(
                     document.MemoryImages,
                     static item => item.Id.Value,
                     writer.CancellationToken))
        {
            writer.WriteStartObject();
            writer.WriteString("id", image.Id.Value);
            writer.WriteString("displayName", image.DisplayName);
            writer.WriteNumber("wordWidth", image.Width);
            writer.WriteString(
                "depth",
                ((ulong)image.Depth).ToString(CultureInfo.InvariantCulture));
            writer.WriteString("partPath", $"memory/{image.Id.Value}.bin");
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteDefinition(
        CanonicalJsonWriter writer,
        CircuitDefinition definition)
    {
        writer.WriteStartObject();
        writer.WriteString("id", definition.Id.Value);
        writer.WriteString("displayName", definition.DisplayName);
        writer.WritePropertyName("ports");
        writer.WriteStartArray();
        foreach (var port in definition.Ports)
        {
            writer.WriteStartObject();
            writer.WriteString("id", port.Id.Value);
            writer.WriteString("displayName", port.DisplayName);
            writer.WriteString(
                "direction",
                port.Direction switch
                {
                    PortDirection.Input => "input",
                    PortDirection.Output => "output",
                    _ => throw new InvalidOperationException("Unknown port direction."),
                });
            writer.WriteNumber("width", port.Width);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WritePropertyName("componentInstances");
        writer.WriteStartArray();
        foreach (var instance in OrderById(
                     definition.ComponentInstances,
                     static item => item.Id.Value,
                     writer.CancellationToken))
        {
            WriteComponentInstance(writer, instance);
        }

        writer.WriteEndArray();
        writer.WritePropertyName("nets");
        writer.WriteStartArray();
        foreach (var net in OrderById(
                     definition.Nets,
                     static item => item.Id.Value,
                     writer.CancellationToken))
        {
            WriteNet(writer, net);
        }

        writer.WriteEndArray();
        writer.WritePropertyName("junctions");
        writer.WriteStartArray();
        foreach (var junction in OrderById(
                     definition.Junctions,
                     static item => item.Id.Value,
                     writer.CancellationToken))
        {
            writer.WriteStartObject();
            writer.WriteString("id", junction.Id.Value);
            writer.WriteString("netId", junction.NetId.Value);
            writer.WritePropertyName("position");
            WritePoint(writer, junction.Position);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WritePropertyName("wireGeometry");
        writer.WriteStartArray();
        foreach (var geometry in OrderById(
                     definition.WireGeometries,
                     static item => item.Id.Value,
                     writer.CancellationToken))
        {
            WriteWireGeometry(writer, geometry);
        }

        writer.WriteEndArray();
        writer.WritePropertyName("presentation");
        WritePresentation(writer, definition);
        writer.WriteEndObject();
    }

    private static void WriteComponentInstance(
        CanonicalJsonWriter writer,
        ComponentInstance instance)
    {
        writer.WriteStartObject();
        writer.WriteString("id", instance.Id.Value);
        if (instance.DisplayName is null)
        {
            writer.WriteNull("displayName");
        }
        else
        {
            writer.WriteString("displayName", instance.DisplayName);
        }

        writer.WritePropertyName("target");
        writer.WriteStartObject();
        switch (instance.Target)
        {
            case LibraryComponentTarget library:
                writer.WriteString("kind", "libraryContract");
                writer.WriteString("libraryId", library.ContractKey.LibraryId);
                writer.WriteString("contractId", library.ContractKey.ContractId);
                break;
            case CircuitDefinitionComponentTarget definition:
                writer.WriteString("kind", "circuitDefinition");
                writer.WriteString(
                    "circuitDefinitionId",
                    definition.CircuitDefinitionId.Value);
                break;
            default:
                throw new InvalidOperationException("Unknown component target.");
        }

        writer.WriteEndObject();
        writer.WritePropertyName("parameters");
        writer.WriteStartArray();
        foreach (var parameter in instance.Parameters)
        {
            writer.WriteStartObject();
            writer.WriteString("parameterId", parameter.ParameterId);
            writer.WritePropertyName("value");
            WriteParameterValue(writer, parameter.Value);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteParameterValue(
        CanonicalJsonWriter writer,
        ComponentParameterValue value)
    {
        writer.WriteStartObject();
        switch (value)
        {
            case Unsigned32ParameterValue unsigned32:
                writer.WriteString("kind", "unsigned32");
                writer.WriteNumber("value", unsigned32.Value);
                break;
            case Unsigned64ParameterValue unsigned64:
                writer.WriteString("kind", "unsigned64");
                writer.WriteString(
                    "decimal",
                    unsigned64.Value.ToString(CultureInfo.InvariantCulture));
                break;
            case ChoiceParameterValue choice:
                writer.WriteString("kind", "enum");
                writer.WriteString("value", choice.Value);
                break;
            case LogicVectorParameterValue vector:
                writer.WriteString("kind", "logicVector");
                writer.WritePropertyName("bits");
                writer.WriteUnescapedAsciiStringValue(
                    vector.Values.Count,
                    index => LogicValueByte(
                        vector.Values[vector.Values.Count - 1 - index]));
                break;
            case WidthsParameterValue widths:
                writer.WriteString("kind", "unsigned32List");
                writer.WritePropertyName("values");
                writer.WriteStartArray();
                foreach (var width in widths.Values)
                {
                    writer.WriteNumberValue(width);
                }

                writer.WriteEndArray();
                break;
            case SlicesParameterValue slices:
                writer.WriteString("kind", "sliceList");
                writer.WritePropertyName("values");
                writer.WriteStartArray();
                foreach (var slice in slices.Values)
                {
                    writer.WriteStartObject();
                    writer.WriteNumber("offset", slice.Offset);
                    writer.WriteNumber("length", slice.Length);
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                break;
            case MemoryImageParameterValue memory:
                writer.WriteString("kind", "memoryImage");
                writer.WriteString("memoryImageId", memory.MemoryImageId.Value);
                break;
            default:
                throw new InvalidOperationException("Unknown parameter value.");
        }

        writer.WriteEndObject();
    }

    private static void WriteNet(CanonicalJsonWriter writer, Net net)
    {
        writer.WriteStartObject();
        writer.WriteString("id", net.Id.Value);
        writer.WriteNumber("width", net.Width);
        writer.WritePropertyName("terminals");
        writer.WriteStartArray();
        foreach (var terminal in net.Terminals)
        {
            writer.WriteStartObject();
            switch (terminal)
            {
                case DefinitionTerminalReference definition:
                    writer.WriteString("kind", "definitionPort");
                    writer.WriteString("portId", definition.DefinitionPortId.Value);
                    break;
                case InstanceTerminalReference instance:
                    writer.WriteString("kind", "instancePort");
                    writer.WriteString(
                        "componentInstanceId",
                        instance.ComponentInstanceId.Value);
                    writer.WriteString("portId", instance.PortId);
                    break;
                default:
                    throw new InvalidOperationException("Unknown terminal reference.");
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WritePropertyName("junctionIds");
        writer.WriteStartArray();
        foreach (var junctionId in net.JunctionIds)
        {
            writer.WriteStringValue(junctionId.Value);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteWireGeometry(
        CanonicalJsonWriter writer,
        WireGeometry geometry)
    {
        writer.WriteStartObject();
        writer.WriteString("id", geometry.Id.Value);
        writer.WriteString("netId", geometry.NetId.Value);
        writer.WritePropertyName("route");
        writer.WriteStartObject();
        switch (geometry.Route)
        {
            case UnroutedWireRoute:
                writer.WriteString("kind", "unrouted");
                break;
            case OrthogonalWireRoute orthogonal:
                writer.WriteString("kind", "orthogonal");
                writer.WritePropertyName("points");
                writer.WriteStartArray();
                foreach (var point in orthogonal.Points)
                {
                    WritePoint(writer, point);
                }

                writer.WriteEndArray();
                break;
            default:
                throw new InvalidOperationException("Unknown wire route.");
        }

        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void WritePresentation(
        CanonicalJsonWriter writer,
        CircuitDefinition definition)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("componentPlacements");
        writer.WriteStartArray();
        foreach (var instance in OrderById(
                     definition.ComponentInstances,
                     static item => item.Id.Value,
                     writer.CancellationToken))
        {
            writer.WriteStartObject();
            writer.WriteString("componentInstanceId", instance.Id.Value);
            writer.WritePropertyName("origin");
            WritePoint(writer, instance.Placement.Origin);
            writer.WritePropertyName("orientation");
            writer.WriteStartObject();
            writer.WriteNumber(
                "quarterTurnsClockwise",
                (int)instance.Placement.QuarterTurnsClockwise);
            writer.WriteBoolean("reflected", instance.Placement.Reflected);
            writer.WriteEndObject();
            if (instance.SymbolVariantId is null)
            {
                writer.WriteNull("symbolVariantId");
            }
            else
            {
                writer.WriteString("symbolVariantId", instance.SymbolVariantId);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WritePropertyName("definitionPortPlacements");
        writer.WriteStartArray();
        foreach (var port in OrderById(
                     definition.Ports,
                     static item => item.Id.Value,
                     writer.CancellationToken))
        {
            writer.WriteStartObject();
            writer.WriteString("portId", port.Id.Value);
            writer.WritePropertyName("position");
            WritePoint(writer, port.Placement.Position);
            writer.WriteString(
                "facing",
                port.Placement.Facing switch
                {
                    CardinalDirection.North => "north",
                    CardinalDirection.East => "east",
                    CardinalDirection.South => "south",
                    CardinalDirection.West => "west",
                    _ => throw new InvalidOperationException("Unknown facing direction."),
                });
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WritePropertyName("annotations");
        writer.WriteStartArray();
        foreach (var annotation in definition.Annotations)
        {
            writer.WriteStartObject();
            writer.WriteString("id", annotation.Id.Value);
            writer.WriteString("text", annotation.Text);
            writer.WritePropertyName("position");
            WritePoint(writer, annotation.Position);
            writer.WriteString(
                "alignment",
                annotation.Alignment switch
                {
                    AnnotationAlignment.Start => "start",
                    AnnotationAlignment.Center => "center",
                    AnnotationAlignment.End => "end",
                    _ => throw new InvalidOperationException("Unknown annotation alignment."),
                });
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WritePoint(CanonicalJsonWriter writer, GridPoint point)
    {
        writer.WriteStartObject();
        writer.WriteNumber("x", point.X);
        writer.WriteNumber("y", point.Y);
        writer.WriteEndObject();
    }

    private static byte LogicValueByte(LogicValue value) => value switch
    {
        LogicValue.Zero => (byte)'0',
        LogicValue.One => (byte)'1',
        LogicValue.X => (byte)'X',
        _ => throw new InvalidOperationException(
            "An authored logic vector cannot contain high impedance."),
    };

    private static T[] OrderById<T>(
        IReadOnlyList<T> items,
        Func<T, string> selectId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var ordered = new T[items.Count];
        for (var index = 0; index < items.Count; index++)
        {
            if ((index & 0x3ff) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            ordered[index] = items[index];
        }

        var comparisons = 0;
        try
        {
            Array.Sort(ordered, (left, right) =>
            {
                comparisons++;
                if ((comparisons & 0x3ff) == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                return string.CompareOrdinal(selectId(left), selectId(right));
            });
        }
        catch (InvalidOperationException exception)
            when (exception.InnerException is OperationCanceledException cancellation
                && cancellation.CancellationToken == cancellationToken
                && cancellationToken.IsCancellationRequested)
        {
            ExceptionDispatchInfo.Throw(exception.InnerException);
        }

        return ordered;
    }
}
