using System.Text.Json.Serialization;

namespace LogicLab.ProjectFormat;

internal sealed record PackageManifestDtoV1(
    string Format,
    ulong SchemaVersion,
    ManifestPartDtoV1 ProjectPart,
    ManifestMemoryPartDtoV1[] MemoryParts,
    string PackageDigest);

internal sealed record ManifestPartDtoV1(
    string Path,
    ulong Length,
    string Sha256);

internal sealed record ManifestMemoryPartDtoV1(
    string MemoryImageId,
    string Path,
    ulong Length,
    string Sha256);

internal sealed record ProjectDocumentDtoV1(
    string ProjectId,
    string DisplayName,
    SymbolProfileRefDtoV1 SymbolProfile,
    LibraryReferenceDtoV1[] LibraryReferences,
    string EntryCircuitDefinitionId,
    CircuitDefinitionDtoV1[] CircuitDefinitions,
    MemoryImageRefDtoV1[] MemoryImages);

internal sealed record SymbolProfileRefDtoV1(
    string Id,
    string Version,
    string IndicationConvention);

internal sealed record LibraryReferenceDtoV1(
    string Id,
    string Version,
    string Digest);

internal sealed record MemoryImageRefDtoV1(
    string Id,
    string DisplayName,
    uint WordWidth,
    string Depth,
    string PartPath);

internal sealed record CircuitDefinitionDtoV1(
    string Id,
    string DisplayName,
    DefinitionPortDtoV1[] Ports,
    ComponentInstanceDtoV1[] ComponentInstances,
    NetDtoV1[] Nets,
    JunctionDtoV1[] Junctions,
    WireGeometryDtoV1[] WireGeometry,
    CircuitPresentationDtoV1 Presentation);

internal sealed record DefinitionPortDtoV1(
    string Id,
    string DisplayName,
    string Direction,
    uint Width);

internal sealed record ComponentInstanceDtoV1(
    string Id,
    string? DisplayName,
    ComponentTargetDtoV1 Target,
    ParameterBindingDtoV1[] Parameters);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(LibraryContractTargetDtoV1), "libraryContract")]
[JsonDerivedType(typeof(CircuitDefinitionTargetDtoV1), "circuitDefinition")]
internal abstract record ComponentTargetDtoV1;

internal sealed record LibraryContractTargetDtoV1(
    string LibraryId,
    string ContractId) : ComponentTargetDtoV1;

internal sealed record CircuitDefinitionTargetDtoV1(
    string CircuitDefinitionId) : ComponentTargetDtoV1;

internal sealed record ParameterBindingDtoV1(
    string ParameterId,
    ParameterValueDtoV1 Value);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(Unsigned32ParameterDtoV1), "unsigned32")]
[JsonDerivedType(typeof(Unsigned64ParameterDtoV1), "unsigned64")]
[JsonDerivedType(typeof(EnumParameterDtoV1), "enum")]
[JsonDerivedType(typeof(LogicVectorParameterDtoV1), "logicVector")]
[JsonDerivedType(typeof(Unsigned32ListParameterDtoV1), "unsigned32List")]
[JsonDerivedType(typeof(SliceListParameterDtoV1), "sliceList")]
[JsonDerivedType(typeof(MemoryImageParameterDtoV1), "memoryImage")]
internal abstract record ParameterValueDtoV1;

internal sealed record Unsigned32ParameterDtoV1(uint Value)
    : ParameterValueDtoV1;

internal sealed record Unsigned64ParameterDtoV1(string Decimal)
    : ParameterValueDtoV1;

internal sealed record EnumParameterDtoV1(string Value)
    : ParameterValueDtoV1;

internal sealed record LogicVectorParameterDtoV1(string Bits)
    : ParameterValueDtoV1;

internal sealed record Unsigned32ListParameterDtoV1(uint[] Values)
    : ParameterValueDtoV1;

internal sealed record SliceListParameterDtoV1(BitSliceDtoV1[] Values)
    : ParameterValueDtoV1;

internal sealed record MemoryImageParameterDtoV1(string MemoryImageId)
    : ParameterValueDtoV1;

internal sealed record BitSliceDtoV1(uint Offset, uint Length);

internal sealed record NetDtoV1(
    string Id,
    uint Width,
    TerminalReferenceDtoV1[] Terminals,
    string[] JunctionIds);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(DefinitionPortTerminalDtoV1), "definitionPort")]
[JsonDerivedType(typeof(InstancePortTerminalDtoV1), "instancePort")]
internal abstract record TerminalReferenceDtoV1;

internal sealed record DefinitionPortTerminalDtoV1(string PortId)
    : TerminalReferenceDtoV1;

internal sealed record InstancePortTerminalDtoV1(
    string ComponentInstanceId,
    string PortId) : TerminalReferenceDtoV1;

internal sealed record JunctionDtoV1(
    string Id,
    string NetId,
    GridPointDtoV1 Position);

internal sealed record WireGeometryDtoV1(
    string Id,
    string NetId,
    WireRouteDtoV1 Route);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(UnroutedWireRouteDtoV1), "unrouted")]
[JsonDerivedType(typeof(OrthogonalWireRouteDtoV1), "orthogonal")]
internal abstract record WireRouteDtoV1;

internal sealed record UnroutedWireRouteDtoV1 : WireRouteDtoV1;

internal sealed record OrthogonalWireRouteDtoV1(GridPointDtoV1[] Points)
    : WireRouteDtoV1;

internal readonly record struct GridPointDtoV1(int X, int Y);

internal sealed record CircuitPresentationDtoV1(
    ComponentPlacementDtoV1[] ComponentPlacements,
    DefinitionPortPlacementDtoV1[] DefinitionPortPlacements,
    AnnotationDtoV1[] Annotations);

internal sealed record ComponentPlacementDtoV1(
    string ComponentInstanceId,
    GridPointDtoV1 Origin,
    OrientationDtoV1 Orientation,
    string? SymbolVariantId);

internal sealed record OrientationDtoV1(
    int QuarterTurnsClockwise,
    bool Reflected);

internal sealed record DefinitionPortPlacementDtoV1(
    string PortId,
    GridPointDtoV1 Position,
    string Facing);

internal sealed record AnnotationDtoV1(
    string Id,
    string Text,
    GridPointDtoV1 Position,
    string Alignment);

[JsonSourceGenerationOptions(
    GenerationMode = JsonSourceGenerationMode.Metadata,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(PackageManifestDtoV1))]
[JsonSerializable(typeof(ProjectDocumentDtoV1))]
internal sealed partial class ProjectPackageJsonContext : JsonSerializerContext;
