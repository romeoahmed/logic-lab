using System.Globalization;
using System.Text.Json;
using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;

namespace LogicLab.Infrastructure.Persistence;

internal static class ProjectRevisionPayloadSerializer
{
    private const int SchemaVersion = 2;

    private static readonly ProjectRevisionPayloadJsonContext JsonContext = new(
        new JsonSerializerOptions(JsonSerializerOptions.Strict)
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });

    public static byte[] Serialize(ProjectRevision revision)
    {
        ArgumentNullException.ThrowIfNull(revision);
        return JsonSerializer.SerializeToUtf8Bytes(
            ToPayload(revision),
            JsonContext.ProjectRevisionPayloadV2);
    }

    public static ProjectRevision Deserialize(ReadOnlySpan<byte> payload)
    {
        try
        {
            var stored = JsonSerializer.Deserialize(
                payload,
                JsonContext.ProjectRevisionPayloadV2)
                ?? throw InvalidPayload();
            if (stored.SchemaVersion != SchemaVersion)
            {
                throw InvalidPayload();
            }

            return FromPayload(stored);
        }
        catch (Exception exception) when (exception is
            ArgumentException or InvalidOperationException or OverflowException)
        {
            throw InvalidPayload(exception);
        }
    }

    private static ProjectRevisionPayloadV2 ToPayload(ProjectRevision revision)
    {
        return new ProjectRevisionPayloadV2(
            SchemaVersion,
            revision.RevisionId.Value,
            ToPayload(revision.Document));
    }

    private static ProjectDocumentPayloadV2 ToPayload(ProjectDocument document)
    {
        return new ProjectDocumentPayloadV2(
            document.ProjectId.Value,
            document.DisplayName,
            new LibraryReferencePayloadV2(
                document.LibrarySnapshot.LibraryId,
                document.LibrarySnapshot.Version,
                document.LibrarySnapshot.ContentDigest),
            new SymbolProfilePayloadV2(
                document.SymbolProfile.Id,
                document.SymbolProfile.Version,
                ToToken(document.SymbolProfile.IndicationConvention)),
            document.EntryCircuitDefinitionId.Value,
            [.. document.CircuitDefinitions.Select(ToPayload)],
            [.. document.MemoryImages.Select(ToPayload)]);
    }

    private static CircuitDefinitionPayloadV2 ToPayload(
        CircuitDefinition definition)
    {
        return new CircuitDefinitionPayloadV2(
            definition.Id.Value,
            definition.DisplayName,
            [.. definition.Ports.Select(ToPayload)],
            [.. definition.ComponentInstances.Select(ToPayload)],
            [.. definition.Nets.Select(ToPayload)],
            [.. definition.Junctions.Select(ToPayload)],
            [.. definition.WireGeometries.Select(ToPayload)],
            [.. definition.Annotations.Select(ToPayload)]);
    }

    private static DefinitionPortPayloadV2 ToPayload(DefinitionPort port)
    {
        return new DefinitionPortPayloadV2(
            port.Id.Value,
            port.DisplayName,
            ToToken(port.Direction),
            port.Width,
            new DefinitionPortPlacementPayloadV2(
                ToPayload(port.Placement.Position),
                ToToken(port.Placement.Facing)));
    }

    private static ComponentInstancePayloadV2 ToPayload(
        ComponentInstance instance)
    {
        return new ComponentInstancePayloadV2(
            instance.Id.Value,
            ToPayload(instance.Target),
            [.. instance.Parameters.Select(ToPayload)],
            new ComponentPlacementPayloadV2(
                ToPayload(instance.Placement.Origin),
                (int)instance.Placement.QuarterTurnsClockwise,
                instance.Placement.Reflected),
            instance.DisplayName,
            instance.SymbolVariantId);
    }

    private static ComponentTargetPayloadV2 ToPayload(ComponentTarget target)
    {
        return target switch
        {
            LibraryComponentTarget library => new LibraryComponentTargetPayloadV2(
                library.ContractKey.LibraryId,
                library.ContractKey.ContractId),
            CircuitDefinitionComponentTarget definition =>
                new CircuitDefinitionComponentTargetPayloadV2(
                    definition.CircuitDefinitionId.Value),
            _ => throw new InvalidOperationException(
                "The Component Target variant is undefined."),
        };
    }

    private static ComponentParameterBindingPayloadV2 ToPayload(
        ComponentParameterBinding binding)
    {
        return new ComponentParameterBindingPayloadV2(
            binding.ParameterId,
            ToPayload(binding.Value));
    }

    private static ComponentParameterValuePayloadV2 ToPayload(
        ComponentParameterValue value)
    {
        return value switch
        {
            MemoryImageParameterValue image => new MemoryImageParameterValuePayloadV2(
                image.MemoryImageId.Value),
            Unsigned32ParameterValue unsigned => new Unsigned32ParameterValuePayloadV2(
                unsigned.Value),
            Unsigned64ParameterValue unsigned => new Unsigned64ParameterValuePayloadV2(
                unsigned.Value.ToString(CultureInfo.InvariantCulture)),
            ChoiceParameterValue choice => new ChoiceParameterValuePayloadV2(
                choice.Value),
            LogicVectorParameterValue vector => new LogicVectorParameterValuePayloadV2(
                ToBits(vector.Values)),
            SlicesParameterValue slices => new SlicesParameterValuePayloadV2(
                [.. slices.Values.Select(slice => new BitSlicePayloadV2(
                    slice.Offset,
                    slice.Length))]),
            WidthsParameterValue widths => new WidthsParameterValuePayloadV2(
                [.. widths.Values]),
            _ => throw new InvalidOperationException(
                "The Component Parameter Value variant is undefined."),
        };
    }

    private static NetPayloadV2 ToPayload(Net net)
    {
        return new NetPayloadV2(
            net.Id.Value,
            net.Width,
            [.. net.Terminals.Select(ToPayload)],
            [.. net.JunctionIds.Select(id => id.Value)]);
    }

    private static AuthoredTerminalReferencePayloadV2 ToPayload(
        AuthoredTerminalReference terminal)
    {
        return terminal switch
        {
            DefinitionTerminalReference definition =>
                new DefinitionTerminalReferencePayloadV2(
                    definition.CircuitDefinitionId.Value,
                    definition.DefinitionPortId.Value),
            InstanceTerminalReference instance =>
                new InstanceTerminalReferencePayloadV2(
                    instance.CircuitDefinitionId.Value,
                    instance.ComponentInstanceId.Value,
                    instance.PortId),
            _ => throw new InvalidOperationException(
                "The Terminal Reference variant is undefined."),
        };
    }

    private static JunctionPayloadV2 ToPayload(Junction junction)
    {
        return new JunctionPayloadV2(
            junction.Id.Value,
            junction.NetId.Value,
            ToPayload(junction.Position));
    }

    private static WireGeometryPayloadV2 ToPayload(WireGeometry geometry)
    {
        return new WireGeometryPayloadV2(
            geometry.Id.Value,
            geometry.NetId.Value,
            ToPayload(geometry.Route));
    }

    private static WireRoutePayloadV2 ToPayload(WireRoute route)
    {
        return route switch
        {
            UnroutedWireRoute => new UnroutedWireRoutePayloadV2(),
            OrthogonalWireRoute orthogonal => new OrthogonalWireRoutePayloadV2(
                [.. orthogonal.Points.Select(ToPayload)]),
            _ => throw new InvalidOperationException(
                "The Wire Route variant is undefined."),
        };
    }

    private static AnnotationPayloadV2 ToPayload(Annotation annotation)
    {
        return new AnnotationPayloadV2(
            annotation.Id.Value,
            annotation.Text,
            ToPayload(annotation.Position),
            ToToken(annotation.Alignment));
    }

    private static MemoryImagePayloadV2 ToPayload(MemoryImage image)
    {
        return new MemoryImagePayloadV2(
            image.Id.Value,
            image.DisplayName,
            image.Width,
            image.Depth,
            image.PackedCells.ToArray());
    }

    private static GridPointPayloadV2 ToPayload(GridPoint point) =>
        new(point.X, point.Y);

    private static ProjectRevision FromPayload(ProjectRevisionPayloadV2 payload)
    {
        var document = RequireNotNull(payload.Document);
        var circuitDefinitions = RequireNotNull(document.CircuitDefinitions);
        var memoryImages = RequireNotNull(document.MemoryImages);
        var library = FromPayload(document.Library);
        var projectDocument = new ProjectDocument(
            new ProjectId(RequireValue(document.ProjectId)),
            document.DisplayName,
            library,
            FromPayload(document.SymbolProfile),
            new CircuitDefinitionId(RequireValue(
                document.EntryCircuitDefinitionId)),
            [.. circuitDefinitions.Select(FromPayload)],
            [.. memoryImages.Select(FromPayload)]);
        return ProjectEditor.Rehydrate(
            new ProjectRevisionId(RequireValue(payload.RevisionId)),
            projectDocument);
    }

    private static LibrarySnapshot FromPayload(LibraryReferencePayloadV2 library)
    {
        RequireNotNull(library);
        if (!string.Equals(
                library.LibraryId,
                LibrarySnapshot.Core.LibraryId,
                StringComparison.Ordinal)
            || !string.Equals(
                library.Version,
                LibrarySnapshot.Core.Version,
                StringComparison.Ordinal)
            || !string.Equals(
                library.ContentDigest,
                LibrarySnapshot.Core.ContentDigest,
                StringComparison.Ordinal))
        {
            throw InvalidPayload();
        }

        return LibrarySnapshot.Core;
    }

    private static SymbolProfileReference FromPayload(
        SymbolProfilePayloadV2 profile)
    {
        RequireNotNull(profile);
        return new SymbolProfileReference(
            RequireValue(profile.Id),
            RequireValue(profile.Version),
            profile.IndicationConvention switch
            {
                "negation" => IndicationConvention.Negation,
                "directPolarity" => IndicationConvention.DirectPolarity,
                _ => throw InvalidPayload(),
            });
    }

    private static CircuitDefinition FromPayload(CircuitDefinitionPayloadV2 definition)
    {
        RequireNotNull(definition);
        return new CircuitDefinition(
            new CircuitDefinitionId(RequireValue(definition.Id)),
            definition.DisplayName,
            [.. RequireNotNull(definition.Ports).Select(FromPayload)],
            [.. RequireNotNull(definition.ComponentInstances).Select(FromPayload)],
            [.. RequireNotNull(definition.Nets).Select(FromPayload)],
            [.. RequireNotNull(definition.Junctions).Select(FromPayload)],
            [.. RequireNotNull(definition.WireGeometries).Select(FromPayload)],
            [.. RequireNotNull(definition.Annotations).Select(FromPayload)]);
    }

    private static DefinitionPort FromPayload(DefinitionPortPayloadV2 port)
    {
        RequireNotNull(port);
        var placement = RequireNotNull(port.Placement);
        return new DefinitionPort(
            new DefinitionPortId(RequireValue(port.Id)),
            port.DisplayName,
            port.Direction switch
            {
                "input" => PortDirection.Input,
                "output" => PortDirection.Output,
                _ => throw InvalidPayload(),
            },
            port.Width,
            new DefinitionPortPlacement(
                FromPayload(placement.Position),
                placement.Facing switch
                {
                    "north" => CardinalDirection.North,
                    "east" => CardinalDirection.East,
                    "south" => CardinalDirection.South,
                    "west" => CardinalDirection.West,
                    _ => throw InvalidPayload(),
                }));
    }

    private static ComponentInstance FromPayload(ComponentInstancePayloadV2 instance)
    {
        RequireNotNull(instance);
        var placement = RequireNotNull(instance.Placement);
        return new ComponentInstance(
            new ComponentInstanceId(RequireValue(instance.Id)),
            FromPayload(instance.Target),
            [.. RequireNotNull(instance.Parameters).Select(FromPayload)],
            new ComponentPlacement(
                FromPayload(placement.Origin),
                placement.QuarterTurnsClockwise switch
                {
                    0 => QuarterTurn.Zero,
                    1 => QuarterTurn.One,
                    2 => QuarterTurn.Two,
                    3 => QuarterTurn.Three,
                    _ => throw InvalidPayload(),
                },
                placement.Reflected),
            instance.DisplayName,
            instance.SymbolVariantId);
    }

    private static ComponentTarget FromPayload(ComponentTargetPayloadV2 target)
    {
        return target switch
        {
            LibraryComponentTargetPayloadV2 library => new LibraryComponentTarget(
                new ComponentContractKey(
                    RequireValue(library.LibraryId),
                    RequireValue(library.ContractId))),
            CircuitDefinitionComponentTargetPayloadV2 definition =>
                new CircuitDefinitionComponentTarget(
                    new CircuitDefinitionId(RequireValue(
                        definition.CircuitDefinitionId))),
            _ => throw InvalidPayload(),
        };
    }

    private static ComponentParameterBinding FromPayload(
        ComponentParameterBindingPayloadV2 binding)
    {
        RequireNotNull(binding);
        return new ComponentParameterBinding(
            RequireValue(binding.ParameterId),
            FromPayload(binding.Value));
    }

    private static ComponentParameterValue FromPayload(
        ComponentParameterValuePayloadV2 value)
    {
        return value switch
        {
            MemoryImageParameterValuePayloadV2 image => new MemoryImageParameterValue(
                new MemoryImageId(RequireValue(image.MemoryImageId))),
            Unsigned32ParameterValuePayloadV2 unsigned =>
                new Unsigned32ParameterValue(unsigned.Value),
            Unsigned64ParameterValuePayloadV2 unsigned =>
                new Unsigned64ParameterValue(ParseUnsigned64(unsigned.Decimal)),
            ChoiceParameterValuePayloadV2 choice =>
                new ChoiceParameterValue(RequireValue(choice.Value)),
            LogicVectorParameterValuePayloadV2 vector =>
                new LogicVectorParameterValue(FromBits(vector.Bits)),
            SlicesParameterValuePayloadV2 slices => new SlicesParameterValue(
                [.. RequireNotNull(slices.Values).Select(FromPayload)]),
            WidthsParameterValuePayloadV2 widths =>
                new WidthsParameterValue(RequireNotNull(widths.Values)),
            _ => throw InvalidPayload(),
        };
    }

    private static Net FromPayload(NetPayloadV2 net)
    {
        RequireNotNull(net);
        return new Net(
            new NetId(RequireValue(net.Id)),
            net.Width,
            [.. RequireNotNull(net.Terminals).Select(FromPayload)],
            [.. RequireNotNull(net.JunctionIds)
                .Select(id => new JunctionId(RequireValue(id)))]);
    }

    private static BitSlice FromPayload(BitSlicePayloadV2 slice)
    {
        RequireNotNull(slice);
        return new BitSlice(slice.Offset, slice.Length);
    }

    private static AuthoredTerminalReference FromPayload(
        AuthoredTerminalReferencePayloadV2 terminal)
    {
        return terminal switch
        {
            DefinitionTerminalReferencePayloadV2 definition =>
                new DefinitionTerminalReference(
                    new CircuitDefinitionId(RequireValue(
                        definition.CircuitDefinitionId)),
                    new DefinitionPortId(RequireValue(
                        definition.DefinitionPortId))),
            InstanceTerminalReferencePayloadV2 instance =>
                new InstanceTerminalReference(
                    new CircuitDefinitionId(RequireValue(
                        instance.CircuitDefinitionId)),
                    new ComponentInstanceId(RequireValue(
                        instance.ComponentInstanceId)),
                    RequireValue(instance.PortId)),
            _ => throw InvalidPayload(),
        };
    }

    private static Junction FromPayload(JunctionPayloadV2 junction)
    {
        RequireNotNull(junction);
        return new Junction(
            new JunctionId(RequireValue(junction.Id)),
            new NetId(RequireValue(junction.NetId)),
            FromPayload(junction.Position));
    }

    private static WireGeometry FromPayload(WireGeometryPayloadV2 geometry)
    {
        RequireNotNull(geometry);
        return new WireGeometry(
            new WireGeometryId(RequireValue(geometry.Id)),
            new NetId(RequireValue(geometry.NetId)),
            FromPayload(geometry.Route));
    }

    private static WireRoute FromPayload(WireRoutePayloadV2 route)
    {
        return route switch
        {
            UnroutedWireRoutePayloadV2 => new UnroutedWireRoute(),
            OrthogonalWireRoutePayloadV2 orthogonal => new OrthogonalWireRoute(
                [.. RequireNotNull(orthogonal.Points).Select(FromPayload)]),
            _ => throw InvalidPayload(),
        };
    }

    private static Annotation FromPayload(AnnotationPayloadV2 annotation)
    {
        RequireNotNull(annotation);
        return new Annotation(
            new AnnotationId(RequireValue(annotation.Id)),
            new AnnotationValue(
                annotation.Text,
                FromPayload(annotation.Position),
                annotation.Alignment switch
                {
                    "start" => AnnotationAlignment.Start,
                    "center" => AnnotationAlignment.Center,
                    "end" => AnnotationAlignment.End,
                    _ => throw InvalidPayload(),
                }));
    }

    private static MemoryImage FromPayload(MemoryImagePayloadV2 image)
    {
        RequireNotNull(image);
        return new MemoryImage(
            new MemoryImageId(RequireValue(image.Id)),
            image.DisplayName,
            image.Width,
            image.Depth,
            RequireNotNull(image.PackedCells));
    }

    private static GridPoint FromPayload(GridPointPayloadV2 point) =>
        new(point.X, point.Y);

    private static string ToBits(IEnumerable<LogicValue> values) =>
        new([.. values.Select(ToToken)]);

    private static LogicValue[] FromBits(string bits)
    {
        ArgumentNullException.ThrowIfNull(bits);
        return [.. bits.Select(bit => bit switch
        {
            '0' => LogicValue.Zero,
            '1' => LogicValue.One,
            'X' => LogicValue.X,
            'Z' => LogicValue.Z,
            _ => throw InvalidPayload(),
        })];
    }

    private static ulong ParseUnsigned64(string value)
    {
        if (!ulong.TryParse(
                value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var parsed)
            || !string.Equals(
                value,
                parsed.ToString(CultureInfo.InvariantCulture),
                StringComparison.Ordinal))
        {
            throw InvalidPayload();
        }

        return parsed;
    }

    private static char ToToken(LogicValue value)
    {
        return value switch
        {
            LogicValue.Zero => '0',
            LogicValue.One => '1',
            LogicValue.X => 'X',
            LogicValue.Z => 'Z',
            _ => throw new InvalidOperationException(
                "The Logic Value variant is undefined."),
        };
    }

    private static string ToToken(IndicationConvention convention)
    {
        return convention switch
        {
            IndicationConvention.Negation => "negation",
            IndicationConvention.DirectPolarity => "directPolarity",
            _ => throw new InvalidOperationException(
                "The Indication Convention variant is undefined."),
        };
    }

    private static string ToToken(PortDirection direction)
    {
        return direction switch
        {
            PortDirection.Input => "input",
            PortDirection.Output => "output",
            _ => throw new InvalidOperationException(
                "The Port Direction variant is undefined."),
        };
    }

    private static string ToToken(CardinalDirection direction)
    {
        return direction switch
        {
            CardinalDirection.North => "north",
            CardinalDirection.East => "east",
            CardinalDirection.South => "south",
            CardinalDirection.West => "west",
            _ => throw new InvalidOperationException(
                "The Cardinal Direction variant is undefined."),
        };
    }

    private static string ToToken(AnnotationAlignment alignment)
    {
        return alignment switch
        {
            AnnotationAlignment.Start => "start",
            AnnotationAlignment.Center => "center",
            AnnotationAlignment.End => "end",
            _ => throw new InvalidOperationException(
                "The Annotation Alignment variant is undefined."),
        };
    }

    private static string RequireValue(string value)
    {
        return !string.IsNullOrEmpty(value) ? value : throw InvalidPayload();
    }

    private static T RequireNotNull<T>(T? value)
        where T : class
    {
        return value ?? throw InvalidPayload();
    }

    private static JsonException InvalidPayload(Exception? innerException = null) =>
        new("The stored Project Revision payload is invalid or unsupported.", innerException);
}
