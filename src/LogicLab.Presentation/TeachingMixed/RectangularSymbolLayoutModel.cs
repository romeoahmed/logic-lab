using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.Presentation.Geometry;

namespace LogicLab.Presentation.TeachingMixed;

internal sealed record RectangularSymbolPort(
    string Id,
    string DisplayName,
    PortDirection Direction,
    uint Width);

internal sealed record RectangularSymbolPortFunction(
    string PortId,
    string? Text,
    bool IsComplementedOutput = false);

internal sealed record RectangularSymbolActiveLowInputQualifier(string PortId);

internal sealed record RectangularSymbolInputFunctionQualifier(
    RectangularSymbolInputFunctionKind Kind,
    string PortId,
    string Text,
    string ClauseId);

internal enum RectangularSymbolDynamicInputKind
{
    RisingEdge,
    FallingEdge,
}

internal sealed record RectangularSymbolDynamicInputQualifier(
    string PortId,
    RectangularSymbolDynamicInputKind Kind);

internal sealed record RectangularSymbolBitGroupingInputQualifier(
    string PortId,
    uint FirstWeight,
    uint LastWeight,
    RectangularSymbolDependencyKind DependencyKind,
    RectangularSymbolDependencyIdentifierRange IdentifierRange);

internal sealed record RectangularSymbolThreeStateOutputQualifier(string PortId);

internal sealed record RectangularSymbolLayoutRequest(
    string? FunctionText,
    FontRoleV1 FunctionFontRole,
    RectangularSymbolDependency[] Dependencies,
    SymbolMetricSetV1 MetricSet,
    PresentationLocaleIdV1 LocaleId,
    BaseDirectionV1 BaseDirection,
    SymbolFacingV1 Facing,
    bool IsReflected,
    IndicationConvention IndicationConvention,
    RectangularSymbolInputFunctionQualifier[] InputFunctionQualifiers,
    RectangularSymbolBitGroupingInputQualifier[] BitGroupingInputQualifiers,
    RectangularSymbolPortFunction[] PortFunctions,
    RectangularSymbolDynamicInputQualifier[] DynamicInputQualifiers,
    RectangularSymbolActiveLowInputQualifier[] ActiveLowInputQualifiers,
    RectangularSymbolThreeStateOutputQualifier[] ThreeStateOutputQualifiers,
    ConformanceEvidenceV1 Conformance);
