using System.Buffers.Binary;
using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;

namespace LogicLab.ProjectFormat;

public static partial class ProjectPackage
{
    private static ProjectImportCandidate TranslateProject(
        ProjectDocumentDtoV1 project,
        IReadOnlyDictionary<string, PackagePart> memoryParts,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequireOpaqueId(project.ProjectId);
            RequireOpaqueId(project.EntryCircuitDefinitionId);
            if (project.LibraryReferences.Length != 1)
            {
                throw Invalid("package_domain_invalid", ("rule", "libraryReference"));
            }

            var library = project.LibraryReferences[0];
            if (!string.Equals(library.Id, LibrarySnapshot.Core.LibraryId, StringComparison.Ordinal)
                || !string.Equals(library.Version, LibrarySnapshot.Core.Version, StringComparison.Ordinal)
                || !string.Equals(library.Digest, LibrarySnapshot.Core.ContentDigest, StringComparison.Ordinal))
            {
                throw Invalid("package_domain_invalid", ("rule", "librarySnapshot"));
            }

            var symbolProfile = new SymbolProfileReference(
                RequireStableName(project.SymbolProfile.Id),
                RequireStableVersion(project.SymbolProfile.Version),
                project.SymbolProfile.IndicationConvention switch
                {
                    "negation" => IndicationConvention.Negation,
                    "directPolarity" => IndicationConvention.DirectPolarity,
                    _ => throw Invalid("package_json_invalid", ("rule", "indicationConvention")),
                });
            var memoryImages = Map(
                OrderedById(
                    project.MemoryImages,
                    item => item.Id,
                    cancellationToken),
                item => TranslateMemoryImage(
                    item,
                    memoryParts,
                    cancellationToken),
                cancellationToken);

            EnsureDistinct(
                project.CircuitDefinitions,
                item => item.Id,
                "circuitDefinition",
                cancellationToken);
            var definitions = Map(
                OrderedById(
                    project.CircuitDefinitions,
                    item => item.Id,
                    cancellationToken),
                item => TranslateDefinition(item, cancellationToken),
                cancellationToken);
            var document = new ProjectDocument(
                new ProjectId(project.ProjectId),
                project.DisplayName,
                LibrarySnapshot.Core,
                symbolProfile,
                new CircuitDefinitionId(project.EntryCircuitDefinitionId),
                definitions,
                memoryImages);
            return new ProjectImportCandidate(document, cancellationToken);
        }
        catch (PackageReadInvalidException)
        {
            throw;
        }
        catch (Exception exception) when (exception is
            ArgumentException or InvalidOperationException or OverflowException)
        {
            throw Invalid("package_domain_invalid", ("rule", "authoringInvariant"));
        }
    }

    private static MemoryImage TranslateMemoryImage(
        MemoryImageRefDtoV1 image,
        IReadOnlyDictionary<string, PackagePart> memoryParts,
        CancellationToken cancellationToken)
    {
        RequireOpaqueId(image.Id);
        var depth = ParseCanonicalUnsigned64(image.Depth, "depth");
        if (depth is 0 or > uint.MaxValue || image.WordWidth == 0)
        {
            throw Invalid("package_memory_invalid", ("rule", "shape"));
        }

        return DecodeMemoryImage(
            image,
            checked((uint)depth),
            memoryParts[image.Id].Bytes,
            cancellationToken);
    }

    private static void ValidateMemoryPartAgreement(
        ProjectDocumentDtoV1 project,
        PackageManifestDtoV1 manifest,
        CancellationToken cancellationToken)
    {
        var projectMemoryIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var image in project.MemoryImages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = RequireOpaqueId(image.Id);
            if (!projectMemoryIds.Add(id))
            {
                throw Invalid(
                    "package_domain_invalid",
                    ("rule", "duplicateMemoryImage"));
            }

            if (!string.Equals(
                    image.PartPath,
                    $"memory/{id}.bin",
                    StringComparison.Ordinal))
            {
                throw Invalid(
                    "package_integrity_mismatch",
                    ("partKind", "memory"),
                    ("check", "agreement"));
            }
        }

        if (manifest.MemoryParts.Length != projectMemoryIds.Count)
        {
            throw Invalid(
                "package_integrity_mismatch",
                ("partKind", "memory"),
                ("check", "agreement"));
        }

        foreach (var part in manifest.MemoryParts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!projectMemoryIds.Contains(part.MemoryImageId))
            {
                throw Invalid(
                    "package_integrity_mismatch",
                    ("partKind", "memory"),
                    ("check", "agreement"));
            }
        }
    }

    private static MemoryImage DecodeMemoryImage(
        MemoryImageRefDtoV1 reference,
        uint depth,
        byte[] bytes,
        CancellationToken cancellationToken)
    {
        if (bytes.Length < 20
            || !bytes.AsSpan(0, 4).SequenceEqual("LLMI"u8)
            || BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(4, 2)) != 1
            || bytes[6] != 1
            || bytes[7] != 0)
        {
            throw Invalid("package_memory_invalid", ("rule", "header"));
        }

        var width = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8, 4));
        var encodedDepth = BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(12, 8));
        if (width != reference.WordWidth || encodedDepth != depth)
        {
            throw Invalid("package_memory_invalid", ("rule", "shapeAgreement"));
        }

        var cellCount = checked((ulong)width * depth);
        var payloadLength = checked((cellCount + 3) / 4);
        if (checked(20UL + payloadLength) != checked((ulong)bytes.Length))
        {
            throw Invalid("package_memory_invalid", ("rule", "payloadLength"));
        }

        var payload = bytes.AsSpan(20);
        for (var index = 0; index < payload.Length; index++)
        {
            if ((index & (CancellationInterval - 1)) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var value = payload[index];
            if (index == payload.Length - 1
                && cellCount % 4 is var payloadUsedFields and not 0)
            {
                var usedBits = checked((int)payloadUsedFields * 2);
                value &= checked((byte)((1 << usedBits) - 1));
            }

            if ((value & (value >> 1) & 0x55) != 0)
            {
                throw Invalid("package_memory_invalid", ("rule", "reservedCell"));
            }
        }

        var usedFields = checked((int)(cellCount % 4));
        if (usedFields != 0)
        {
            var usedBits = checked(usedFields * 2);
            var unusedMask = unchecked((byte)~((1 << usedBits) - 1));
            if ((bytes[^1] & unusedMask) != 0)
            {
                throw Invalid("package_memory_invalid", ("rule", "tailFields"));
            }
        }

        return new MemoryImage(
            new MemoryImageId(reference.Id),
            reference.DisplayName,
            width,
            depth,
            payload,
            cancellationToken);
    }

    private static CircuitDefinition TranslateDefinition(
        CircuitDefinitionDtoV1 definition,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequireOpaqueId(definition.Id);
        var definitionId = new CircuitDefinitionId(definition.Id);
        EnsureDistinct(
            definition.Ports,
            item => item.Id,
            "definitionPort",
            cancellationToken);
        EnsureDistinct(
            definition.ComponentInstances,
            item => item.Id,
            "componentInstance",
            cancellationToken);
        EnsureDistinct(
            definition.Nets,
            item => item.Id,
            "net",
            cancellationToken);
        EnsureDistinct(
            definition.Junctions,
            item => item.Id,
            "junction",
            cancellationToken);
        EnsureDistinct(
            definition.WireGeometry,
            item => item.Id,
            "wireGeometry",
            cancellationToken);
        EnsureDistinct(
            definition.Presentation.Annotations,
            item => item.Id,
            "annotation",
            cancellationToken);
        var portPlacements = UniqueBy(
            definition.Presentation.DefinitionPortPlacements,
            item => item.PortId,
            "definitionPortPlacement",
            cancellationToken);
        var componentPlacements = UniqueBy(
            definition.Presentation.ComponentPlacements,
            item => item.ComponentInstanceId,
            "componentPlacement",
            cancellationToken);
        var ports = Map(definition.Ports, port =>
        {
            RequireOpaqueId(port.Id);
            if (!portPlacements.TryGetValue(port.Id, out var placement))
            {
                throw Invalid("package_domain_invalid", ("rule", "portPlacement"));
            }

            return new DefinitionPort(
                new DefinitionPortId(port.Id),
                port.DisplayName,
                port.Direction switch
                {
                    "input" => PortDirection.Input,
                    "output" => PortDirection.Output,
                    _ => throw Invalid("package_json_invalid", ("rule", "portDirection")),
                },
                port.Width,
                new DefinitionPortPlacement(
                    ToPoint(placement.Position),
                    ToCardinalDirection(placement.Facing)));
        }, cancellationToken);
        if (portPlacements.Count != ports.Length)
        {
            throw Invalid("package_domain_invalid", ("rule", "portPlacement"));
        }

        var components = Map(
            OrderedById(
                definition.ComponentInstances,
                item => item.Id,
                cancellationToken),
            instance =>
        {
            RequireOpaqueId(instance.Id);
            if (!componentPlacements.TryGetValue(instance.Id, out var placement))
            {
                throw Invalid("package_domain_invalid", ("rule", "componentPlacement"));
            }

            return new ComponentInstance(
                new ComponentInstanceId(instance.Id),
                TranslateTarget(instance.Target),
                TranslateParameters(instance.Parameters, cancellationToken),
                new ComponentPlacement(
                    ToPoint(placement.Origin),
                    placement.Orientation.QuarterTurnsClockwise switch
                    {
                        0 => QuarterTurn.Zero,
                        1 => QuarterTurn.One,
                        2 => QuarterTurn.Two,
                        3 => QuarterTurn.Three,
                        _ => throw Invalid("package_json_invalid", ("rule", "orientation")),
                    },
                    placement.Orientation.Reflected),
                instance.DisplayName,
                placement.SymbolVariantId);
        }, cancellationToken);
        if (componentPlacements.Count != components.Length)
        {
            throw Invalid("package_domain_invalid", ("rule", "componentPlacement"));
        }

        var nets = Map(
            OrderedById(
                definition.Nets,
                item => item.Id,
                cancellationToken),
            net =>
        {
            var terminals = Map(
                net.Terminals,
                terminal => TranslateTerminal(
                    definitionId,
                    terminal),
                cancellationToken);
            var junctionIds = Map(
                net.JunctionIds,
                id => new JunctionId(RequireOpaqueId(id)),
                cancellationToken);
            return new Net(
                new NetId(RequireOpaqueId(net.Id)),
                net.Width,
                terminals,
                junctionIds);
        }, cancellationToken);

        var junctions = Map(
            OrderedById(
                definition.Junctions,
                item => item.Id,
                cancellationToken),
            junction => new Junction(
                new JunctionId(RequireOpaqueId(junction.Id)),
                new NetId(RequireOpaqueId(junction.NetId)),
                ToPoint(junction.Position)),
            cancellationToken);

        var geometries = Map(
            OrderedById(
                definition.WireGeometry,
                item => item.Id,
                cancellationToken),
            geometry => new WireGeometry(
                new WireGeometryId(RequireOpaqueId(geometry.Id)),
                new NetId(RequireOpaqueId(geometry.NetId)),
                TranslateRoute(geometry.Route, cancellationToken)),
            cancellationToken);

        var annotations = Map(
            definition.Presentation.Annotations,
            annotation => new Annotation(
                new AnnotationId(RequireOpaqueId(annotation.Id)),
                new AnnotationValue(
                    annotation.Text,
                    ToPoint(annotation.Position),
                    annotation.Alignment switch
                    {
                        "start" => AnnotationAlignment.Start,
                        "center" => AnnotationAlignment.Center,
                        "end" => AnnotationAlignment.End,
                        _ => throw Invalid("package_json_invalid", ("rule", "annotationAlignment")),
                    })),
            cancellationToken);
        return new CircuitDefinition(
            definitionId,
            definition.DisplayName,
            ports,
            components,
            nets,
            junctions,
            geometries,
            annotations);
    }

    private static ComponentTarget TranslateTarget(ComponentTargetDtoV1 target) =>
        target switch
        {
            LibraryContractTargetDtoV1 library => new LibraryComponentTarget(
                new ComponentContractKey(
                    RequireStableName(library.LibraryId),
                    RequireStableName(library.ContractId))),
            CircuitDefinitionTargetDtoV1 definition =>
                new CircuitDefinitionComponentTarget(
                    new CircuitDefinitionId(
                        RequireOpaqueId(definition.CircuitDefinitionId))),
            _ => throw Invalid("package_unknown_discriminator"),
        };

    private static ComponentParameterBinding[] TranslateParameters(
        ParameterBindingDtoV1[] bindings,
        CancellationToken cancellationToken) => Map(
            bindings,
            binding => TranslateParameter(binding, cancellationToken),
            cancellationToken);

    private static ComponentParameterBinding TranslateParameter(
        ParameterBindingDtoV1 binding,
        CancellationToken cancellationToken)
    {
        return new ComponentParameterBinding(
            RequireStableName(binding.ParameterId),
            binding.Value switch
            {
                Unsigned32ParameterDtoV1 value =>
                    new Unsigned32ParameterValue(value.Value),
                Unsigned64ParameterDtoV1 value =>
                    new Unsigned64ParameterValue(
                        ParseCanonicalUnsigned64(value.Decimal, "unsigned64")),
                EnumParameterDtoV1 value =>
                    new ChoiceParameterValue(RequireStableName(value.Value)),
                LogicVectorParameterDtoV1 value =>
                    new LogicVectorParameterValue(ParseLogicVector(
                        value.Bits,
                        cancellationToken)),
                Unsigned32ListParameterDtoV1 value =>
                    new WidthsParameterValue(value.Values),
                SliceListParameterDtoV1 value => new SlicesParameterValue(
                    TranslateSlices(value.Values, cancellationToken)),
                MemoryImageParameterDtoV1 value => new MemoryImageParameterValue(
                    new MemoryImageId(RequireOpaqueId(value.MemoryImageId))),
                _ => throw Invalid("package_unknown_discriminator"),
            });
    }

    private static BitSlice[] TranslateSlices(
        BitSliceDtoV1[] slices,
        CancellationToken cancellationToken) => Map(
            slices,
            slice => new BitSlice(slice.Offset, slice.Length),
            cancellationToken);

    private static LogicValue[] ParseLogicVector(
        string bits,
        CancellationToken cancellationToken)
    {
        if (bits.Length == 0)
        {
            throw Invalid("package_json_invalid", ("rule", "logicVector"));
        }

        var values = new LogicValue[bits.Length];
        for (var index = 0; index < bits.Length; index++)
        {
            if ((index & (CancellationInterval - 1)) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            values[bits.Length - 1 - index] = bits[index] switch
            {
                '0' => LogicValue.Zero,
                '1' => LogicValue.One,
                'X' => LogicValue.X,
                _ => throw Invalid("package_json_invalid", ("rule", "logicVector")),
            };
        }

        return values;
    }

    private static AuthoredTerminalReference TranslateTerminal(
        CircuitDefinitionId definitionId,
        TerminalReferenceDtoV1 terminal) => terminal switch
        {
            DefinitionPortTerminalDtoV1 port => new DefinitionTerminalReference(
                definitionId,
                new DefinitionPortId(RequireOpaqueId(port.PortId))),
            InstancePortTerminalDtoV1 instance => new InstanceTerminalReference(
                definitionId,
                new ComponentInstanceId(RequireOpaqueId(instance.ComponentInstanceId)),
                IsOpaqueId(instance.PortId) ? instance.PortId : RequireStableName(instance.PortId)),
            _ => throw Invalid("package_unknown_discriminator"),
        };

    private static WireRoute TranslateRoute(
        WireRouteDtoV1 route,
        CancellationToken cancellationToken) => route switch
        {
            UnroutedWireRouteDtoV1 => new UnroutedWireRoute(),
            OrthogonalWireRouteDtoV1 orthogonal => new OrthogonalWireRoute(
                TranslatePoints(orthogonal.Points, cancellationToken)),
            _ => throw Invalid("package_unknown_discriminator"),
        };

    private static GridPoint[] TranslatePoints(
        GridPointDtoV1[] points,
        CancellationToken cancellationToken) => Map(
            points,
            ToPoint,
            cancellationToken);

    private static GridPoint ToPoint(GridPointDtoV1 point) => new(point.X, point.Y);

    private static CardinalDirection ToCardinalDirection(string value) => value switch
    {
        "north" => CardinalDirection.North,
        "east" => CardinalDirection.East,
        "south" => CardinalDirection.South,
        "west" => CardinalDirection.West,
        _ => throw Invalid("package_json_invalid", ("rule", "facing")),
    };

    private static Dictionary<string, T> UniqueBy<T>(
        IEnumerable<T> values,
        Func<T, string> selectId,
        string entityKind,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = RequireOpaqueId(selectId(value));
            if (!result.TryAdd(id, value))
            {
                throw Invalid("package_domain_invalid", ("rule", $"duplicate{entityKind}"));
            }
        }

        return result;
    }

    private static void EnsureDistinct<T>(
        IEnumerable<T> values,
        Func<T, string> selectId,
        string entityKind,
        CancellationToken cancellationToken)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = RequireOpaqueId(selectId(value));
            if (!ids.Add(id))
            {
                throw Invalid("package_domain_invalid", ("rule", $"duplicate{entityKind}"));
            }
        }
    }

    private static T[] OrderedById<T>(
        IReadOnlyCollection<T> values,
        Func<T, string> selectId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = values.ToArray();
        cancellationToken.ThrowIfCancellationRequested();
        Array.Sort(
            result,
            (left, right) => string.CompareOrdinal(selectId(left), selectId(right)));
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    private static TOutput[] Map<TInput, TOutput>(
        IReadOnlyList<TInput> values,
        Func<TInput, TOutput> transform,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = new TOutput[values.Count];
        for (var index = 0; index < values.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            result[index] = transform(values[index]);
            cancellationToken.ThrowIfCancellationRequested();
        }

        return result;
    }
}
