using System.Text.Json.Serialization;

namespace LogicLab.Infrastructure.Persistence;

internal sealed record ProjectRevisionPayloadV1(
    int SchemaVersion,
    string RevisionId,
    ProjectDocumentPayloadV1 Document);

internal sealed record ProjectDocumentPayloadV1(
    string ProjectId,
    string DisplayName,
    LibraryReferencePayloadV1 Library,
    SymbolProfilePayloadV1 SymbolProfile,
    string EntryCircuitDefinitionId,
    CircuitDefinitionPayloadV1[] CircuitDefinitions,
    MemoryImagePayloadV1[] MemoryImages);

internal sealed record LibraryReferencePayloadV1(
    string LibraryId,
    string Version,
    string ContentDigest);

internal sealed record SymbolProfilePayloadV1(
    string Id,
    string Version,
    string IndicationConvention);

internal sealed record CircuitDefinitionPayloadV1(
    string Id,
    string DisplayName,
    DefinitionPortPayloadV1[] Ports,
    ComponentInstancePayloadV1[] ComponentInstances,
    NetPayloadV1[] Nets,
    JunctionPayloadV1[] Junctions,
    WireGeometryPayloadV1[] WireGeometries,
    AnnotationPayloadV1[] Annotations);

internal sealed record DefinitionPortPayloadV1(
    string Id,
    string DisplayName,
    string Direction,
    uint Width,
    DefinitionPortPlacementPayloadV1 Placement);

internal sealed record DefinitionPortPlacementPayloadV1(
    GridPointPayloadV1 Position,
    string Facing);

internal sealed record ComponentInstancePayloadV1(
    string Id,
    ComponentTargetPayloadV1 Target,
    ComponentParameterBindingPayloadV1[] Parameters,
    ComponentPlacementPayloadV1 Placement,
    string? DisplayName,
    string? SymbolVariantId);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(LibraryComponentTargetPayloadV1), "libraryContract")]
[JsonDerivedType(typeof(CircuitDefinitionComponentTargetPayloadV1), "circuitDefinition")]
internal abstract record ComponentTargetPayloadV1;

internal sealed record LibraryComponentTargetPayloadV1(
    string LibraryId,
    string ContractId) : ComponentTargetPayloadV1;

internal sealed record CircuitDefinitionComponentTargetPayloadV1(
    string CircuitDefinitionId) : ComponentTargetPayloadV1;

internal sealed record ComponentParameterBindingPayloadV1(
    string ParameterId,
    ComponentParameterValuePayloadV1 Value);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(MemoryImageParameterValuePayloadV1), "memoryImage")]
[JsonDerivedType(typeof(Unsigned32ParameterValuePayloadV1), "unsigned32")]
[JsonDerivedType(typeof(Unsigned64ParameterValuePayloadV1), "unsigned64")]
[JsonDerivedType(typeof(ChoiceParameterValuePayloadV1), "choice")]
[JsonDerivedType(typeof(LogicVectorParameterValuePayloadV1), "logicVector")]
[JsonDerivedType(typeof(SlicesParameterValuePayloadV1), "slices")]
[JsonDerivedType(typeof(WidthsParameterValuePayloadV1), "widths")]
internal abstract record ComponentParameterValuePayloadV1;

internal sealed record MemoryImageParameterValuePayloadV1(
    string MemoryImageId) : ComponentParameterValuePayloadV1;

internal sealed record Unsigned32ParameterValuePayloadV1(
    uint Value) : ComponentParameterValuePayloadV1;

internal sealed record Unsigned64ParameterValuePayloadV1(
    string Decimal) : ComponentParameterValuePayloadV1;

internal sealed record ChoiceParameterValuePayloadV1(
    string Value) : ComponentParameterValuePayloadV1;

internal sealed record LogicVectorParameterValuePayloadV1(
    string Bits) : ComponentParameterValuePayloadV1;

internal sealed record SlicesParameterValuePayloadV1(
    BitSlicePayloadV1[] Values) : ComponentParameterValuePayloadV1;

internal sealed record WidthsParameterValuePayloadV1(
    uint[] Values) : ComponentParameterValuePayloadV1;

internal sealed record BitSlicePayloadV1(uint Offset, uint Length);

internal sealed record ComponentPlacementPayloadV1(
    GridPointPayloadV1 Origin,
    int QuarterTurnsClockwise,
    bool Reflected);

internal sealed record NetPayloadV1(
    string Id,
    uint Width,
    AuthoredTerminalReferencePayloadV1[] Terminals,
    string[] JunctionIds);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(DefinitionTerminalReferencePayloadV1), "definitionPort")]
[JsonDerivedType(typeof(InstanceTerminalReferencePayloadV1), "instancePort")]
internal abstract record AuthoredTerminalReferencePayloadV1;

internal sealed record DefinitionTerminalReferencePayloadV1(
    string CircuitDefinitionId,
    string DefinitionPortId) : AuthoredTerminalReferencePayloadV1;

internal sealed record InstanceTerminalReferencePayloadV1(
    string CircuitDefinitionId,
    string ComponentInstanceId,
    string PortId) : AuthoredTerminalReferencePayloadV1;

internal sealed record JunctionPayloadV1(
    string Id,
    string NetId,
    GridPointPayloadV1 Position);

internal sealed record WireGeometryPayloadV1(
    string Id,
    string NetId,
    WireRoutePayloadV1 Route);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(UnroutedWireRoutePayloadV1), "unrouted")]
[JsonDerivedType(typeof(OrthogonalWireRoutePayloadV1), "orthogonal")]
internal abstract record WireRoutePayloadV1;

internal sealed record UnroutedWireRoutePayloadV1 : WireRoutePayloadV1;

internal sealed record OrthogonalWireRoutePayloadV1(
    GridPointPayloadV1[] Points) : WireRoutePayloadV1;

internal readonly record struct GridPointPayloadV1(int X, int Y);

internal sealed record AnnotationPayloadV1(
    string Id,
    string Text,
    GridPointPayloadV1 Position,
    string Alignment);

internal sealed record MemoryImagePayloadV1(
    string Id,
    string DisplayName,
    uint Width,
    uint Depth,
    string[] Words);

[JsonSourceGenerationOptions(
    GenerationMode = JsonSourceGenerationMode.Metadata,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ProjectRevisionPayloadV1))]
internal sealed partial class ProjectRevisionPayloadJsonContext : JsonSerializerContext;
