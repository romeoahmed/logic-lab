using System.Text.Json.Serialization;

namespace LogicLab.Infrastructure.Persistence;

internal sealed record ProjectRevisionPayloadV2(
    int SchemaVersion,
    string RevisionId,
    ProjectDocumentPayloadV2 Document);

internal sealed record ProjectDocumentPayloadV2(
    string ProjectId,
    string DisplayName,
    LibraryReferencePayloadV2 Library,
    SymbolProfilePayloadV2 SymbolProfile,
    string EntryCircuitDefinitionId,
    CircuitDefinitionPayloadV2[] CircuitDefinitions,
    MemoryImagePayloadV2[] MemoryImages);

internal sealed record LibraryReferencePayloadV2(
    string LibraryId,
    string Version,
    string ContentDigest);

internal sealed record SymbolProfilePayloadV2(
    string Id,
    string Version,
    string IndicationConvention);

internal sealed record CircuitDefinitionPayloadV2(
    string Id,
    string DisplayName,
    DefinitionPortPayloadV2[] Ports,
    ComponentInstancePayloadV2[] ComponentInstances,
    NetPayloadV2[] Nets,
    JunctionPayloadV2[] Junctions,
    WireGeometryPayloadV2[] WireGeometries,
    AnnotationPayloadV2[] Annotations);

internal sealed record DefinitionPortPayloadV2(
    string Id,
    string DisplayName,
    string Direction,
    uint Width,
    DefinitionPortPlacementPayloadV2 Placement);

internal sealed record DefinitionPortPlacementPayloadV2(
    GridPointPayloadV2 Position,
    string Facing);

internal sealed record ComponentInstancePayloadV2(
    string Id,
    ComponentTargetPayloadV2 Target,
    ComponentParameterBindingPayloadV2[] Parameters,
    ComponentPlacementPayloadV2 Placement,
    string? DisplayName,
    string? SymbolVariantId);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(LibraryComponentTargetPayloadV2), "libraryContract")]
[JsonDerivedType(typeof(CircuitDefinitionComponentTargetPayloadV2), "circuitDefinition")]
internal abstract record ComponentTargetPayloadV2;

internal sealed record LibraryComponentTargetPayloadV2(
    string LibraryId,
    string ContractId) : ComponentTargetPayloadV2;

internal sealed record CircuitDefinitionComponentTargetPayloadV2(
    string CircuitDefinitionId) : ComponentTargetPayloadV2;

internal sealed record ComponentParameterBindingPayloadV2(
    string ParameterId,
    ComponentParameterValuePayloadV2 Value);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(MemoryImageParameterValuePayloadV2), "memoryImage")]
[JsonDerivedType(typeof(Unsigned32ParameterValuePayloadV2), "unsigned32")]
[JsonDerivedType(typeof(Unsigned64ParameterValuePayloadV2), "unsigned64")]
[JsonDerivedType(typeof(ChoiceParameterValuePayloadV2), "choice")]
[JsonDerivedType(typeof(LogicVectorParameterValuePayloadV2), "logicVector")]
[JsonDerivedType(typeof(SlicesParameterValuePayloadV2), "slices")]
[JsonDerivedType(typeof(WidthsParameterValuePayloadV2), "widths")]
internal abstract record ComponentParameterValuePayloadV2;

internal sealed record MemoryImageParameterValuePayloadV2(
    string MemoryImageId) : ComponentParameterValuePayloadV2;

internal sealed record Unsigned32ParameterValuePayloadV2(
    uint Value) : ComponentParameterValuePayloadV2;

internal sealed record Unsigned64ParameterValuePayloadV2(
    string Decimal) : ComponentParameterValuePayloadV2;

internal sealed record ChoiceParameterValuePayloadV2(
    string Value) : ComponentParameterValuePayloadV2;

internal sealed record LogicVectorParameterValuePayloadV2(
    string Bits) : ComponentParameterValuePayloadV2;

internal sealed record SlicesParameterValuePayloadV2(
    BitSlicePayloadV2[] Values) : ComponentParameterValuePayloadV2;

internal sealed record WidthsParameterValuePayloadV2(
    uint[] Values) : ComponentParameterValuePayloadV2;

internal sealed record BitSlicePayloadV2(uint Offset, uint Length);

internal sealed record ComponentPlacementPayloadV2(
    GridPointPayloadV2 Origin,
    int QuarterTurnsClockwise,
    bool Reflected);

internal sealed record NetPayloadV2(
    string Id,
    uint Width,
    AuthoredTerminalReferencePayloadV2[] Terminals,
    string[] JunctionIds);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(DefinitionTerminalReferencePayloadV2), "definitionPort")]
[JsonDerivedType(typeof(InstanceTerminalReferencePayloadV2), "instancePort")]
internal abstract record AuthoredTerminalReferencePayloadV2;

internal sealed record DefinitionTerminalReferencePayloadV2(
    string CircuitDefinitionId,
    string DefinitionPortId) : AuthoredTerminalReferencePayloadV2;

internal sealed record InstanceTerminalReferencePayloadV2(
    string CircuitDefinitionId,
    string ComponentInstanceId,
    string PortId) : AuthoredTerminalReferencePayloadV2;

internal sealed record JunctionPayloadV2(
    string Id,
    string NetId,
    GridPointPayloadV2 Position);

internal sealed record WireGeometryPayloadV2(
    string Id,
    string NetId,
    WireRoutePayloadV2 Route);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(UnroutedWireRoutePayloadV2), "unrouted")]
[JsonDerivedType(typeof(OrthogonalWireRoutePayloadV2), "orthogonal")]
internal abstract record WireRoutePayloadV2;

internal sealed record UnroutedWireRoutePayloadV2 : WireRoutePayloadV2;

internal sealed record OrthogonalWireRoutePayloadV2(
    GridPointPayloadV2[] Points) : WireRoutePayloadV2;

internal readonly record struct GridPointPayloadV2(int X, int Y);

internal sealed record AnnotationPayloadV2(
    string Id,
    string Text,
    GridPointPayloadV2 Position,
    string Alignment);

internal sealed record MemoryImagePayloadV2(
    string Id,
    string DisplayName,
    uint Width,
    uint Depth,
    byte[] PackedCells);

[JsonSourceGenerationOptions(
    GenerationMode = JsonSourceGenerationMode.Metadata,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ProjectRevisionPayloadV2))]
internal sealed partial class ProjectRevisionPayloadJsonContext : JsonSerializerContext;
