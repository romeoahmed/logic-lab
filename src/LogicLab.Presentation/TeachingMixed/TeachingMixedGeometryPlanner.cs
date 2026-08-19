using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.Presentation.Geometry;

namespace LogicLab.Presentation.TeachingMixed;

public static class TeachingMixedGeometryPlanner
{
    public static GeometryPlanOutcomeV1 Plan(
        ComponentSymbolRequestV1 request,
        ulong maximumPortCount,
        ISymbolTextMeasurerV1 textMeasurer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(textMeasurer);
        ArgumentOutOfRangeException.ThrowIfZero(maximumPortCount);
        if (cancellationToken.IsCancellationRequested)
        {
            return Cancelled();
        }

        try
        {
            var environmentFailure = ValidateEnvironment(request, textMeasurer);
            if (environmentFailure is not null)
            {
                return environmentFailure;
            }

            cancellationToken.ThrowIfCancellationRequested();
            ReadOnlyCollection<ResolvedComponentPortSchema> ports;
            try
            {
                var resolution = request.Contract.ResolvePorts(
                    request.Parameters,
                    cancellationToken);
                if (!resolution.TryMaterialize(
                        maximumPortCount,
                        out ports,
                        cancellationToken))
                {
                    return Invalid(
                        PresentationDiagnosticsV1.ConstraintUnsatisfied(
                            LayoutConstraintV1.PortBudget));
                }
            }
            catch (ArgumentException)
            {
                return Invalid(PresentationDiagnosticsV1.ConstraintUnsatisfied(
                    LayoutConstraintV1.Request));
            }

            GeometryPlanDraft draft;
            string definitionId;
            string definitionVersion;
            string variantId;
            var inputCount = ports.Count(port => port.Direction == PortDirection.Input);
            if (TeachingMixedBasicSymbolRegistry.TryResolve(
                    request.Contract.Key.ContractId,
                    inputCount,
                    request.SymbolVariantId,
                    request.Profile.IndicationConvention,
                    request.Facing,
                    out var basicDefinition))
            {
                ValidateBasicPorts(ports);
                draft = BasicGateGeometryBuilder.Build(
                    request,
                    basicDefinition,
                    ports,
                    textMeasurer,
                    cancellationToken);
                definitionId = basicDefinition.Definition.DefinitionId;
                definitionVersion = basicDefinition.Definition.DefinitionVersion;
                variantId = basicDefinition.VariantId;
            }
            else if (TeachingMixedRectangularSymbolRegistry.TryResolve(
                request.Contract.Key.ContractId,
                request.Parameters,
                ports,
                request.SymbolVariantId,
                out var rectangularDefinition))
            {
                var dynamicInputQualifiers = DynamicInputQualifiers(
                    request.Parameters,
                    ports);
                var inputQualifiers = ActiveLowQualifiers(request.Parameters, ports)
                    .Concat(dynamicInputQualifiers
                        .Where(qualifier => qualifier.IsFallingEdge)
                        .Select(qualifier =>
                            new RectangularSymbolActiveLowInputQualifier(qualifier.PortId)))
                    .ToArray();
                var outputQualifiers = ThreeStateOutputQualifiers(
                    request.Contract.Key.ContractId,
                    ports);
                var layoutRequest = new RectangularSymbolLayoutRequest(
                    rectangularDefinition.FunctionText,
                    rectangularDefinition.FunctionFontRole,
                    rectangularDefinition.AccessibilityKey,
                    rectangularDefinition.Dependencies,
                    request.MetricSet,
                    request.LocaleId,
                    request.BaseDirection,
                    request.Facing,
                    request.IsReflected,
                    request.Profile.IndicationConvention,
                    rectangularDefinition.InputFunctionQualifiers,
                    rectangularDefinition.PortFunctions,
                    dynamicInputQualifiers,
                    inputQualifiers,
                    outputQualifiers,
                    Conformance(
                        rectangularDefinition,
                        dynamicInputQualifiers,
                        inputQualifiers,
                        request.Profile.IndicationConvention,
                        request.Facing));
                var rectangularPorts = ports.Select(port => new RectangularSymbolPort(
                    port.Id,
                    port.Id,
                    port.Direction,
                    port.Width)).ToArray();
                draft = RectangularSymbolGeometryBuilder.Build(
                    layoutRequest,
                    rectangularPorts,
                    textMeasurer,
                    cancellationToken);
                definitionId = rectangularDefinition.DefinitionId;
                definitionVersion = rectangularDefinition.DefinitionVersion;
                variantId = rectangularDefinition.VariantId;
            }
            else
            {
                return VariantUnresolved(request);
            }

            draft = draft with
            {
                Conformance = ApplyAggregatePortConformance(
                    draft.Conformance,
                    ports.Select(port => (port.Id, port.Width))),
            };

            var key = new GeometryPlanKeyV1(
                definitionId,
                definitionVersion,
                request.Contract.SchemaDigest,
                variantId,
                GeometryRequestFingerprint.Compute(request, ports),
                request.Facing,
                request.IsReflected,
                request.Profile.IndicationConvention,
                request.LocaleId,
                request.BaseDirection,
                request.MetricSet.Id,
                request.MetricSet.Version,
                request.MetricSet.Fingerprint,
                request.FontFingerprint);
            return Publish(draft, key, request, cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            return Cancelled();
        }
        catch (LayoutInvalidException exception)
        {
            return Invalid(PresentationDiagnosticsV1.ConstraintUnsatisfied(
                exception.Constraint));
        }
        catch (OverflowException)
        {
            return Invalid(PresentationDiagnosticsV1.ConstraintUnsatisfied(
                LayoutConstraintV1.CoordinateRange));
        }
        catch (Exception exception) when (!PresentationExceptionClassifier.IsFatal(exception))
        {
            return InternalDefect();
        }
    }

    public static GeometryPlanOutcomeV1 Plan(
        CircuitDefinitionSymbolRequestV1 request,
        ulong maximumPortCount,
        ISymbolTextMeasurerV1 textMeasurer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(textMeasurer);
        ArgumentOutOfRangeException.ThrowIfZero(maximumPortCount);
        if (cancellationToken.IsCancellationRequested)
        {
            return Cancelled();
        }

        try
        {
            var environmentFailure = ValidateEnvironment(request, textMeasurer);
            if (environmentFailure is not null)
            {
                return environmentFailure;
            }

            if ((ulong)request.Definition.Ports.Count > maximumPortCount)
            {
                return Invalid(PresentationDiagnosticsV1.ConstraintUnsatisfied(
                    LayoutConstraintV1.PortBudget));
            }

            if (request.SymbolVariantId is not (null or SymbolVariantCatalog.RectangularId))
            {
                return Invalid(PresentationDiagnosticsV1.VariantUnresolved(
                    request.Profile.Id,
                    request.SymbolVariantId));
            }

            var standardReferences = new[]
            {
                new StandardReferenceV1("IEEE-91A", "1991", ["6.1-1", "6.1.2", "6.1.4"]),
            };
            var conformance = ApplyAggregatePortConformance(
                new ConformanceEvidenceV1(
                    ConformanceClaimV1.TeachingExtension,
                    standardReferences,
                    [new ConformanceDeviationV1(
                        "teachingmixed-user-circuit-definition",
                        [.. request.Definition.Ports.Select(port => port.Id.Value)])],
                    AnnexAStatusV1.NotEvaluated),
                request.Definition.Ports.Select(port => (port.Id.Value, port.Width)));
            var layoutRequest = new RectangularSymbolLayoutRequest(
                request.DisplayName,
                FontRoleV1.ExtensionMark,
                "presentation.symbol.circuit-definition",
                [],
                request.MetricSet,
                request.LocaleId,
                request.BaseDirection,
                request.Facing,
                request.IsReflected,
                request.Profile.IndicationConvention,
                InputFunctionQualifiers: [],
                PortFunctions:
                [
                    .. request.Definition.Ports.Select(port =>
                        new RectangularSymbolPortFunction(
                            port.Id.Value,
                            port.DisplayName)),
                ],
                DynamicInputQualifiers: [],
                ActiveLowInputQualifiers: [],
                ThreeStateOutputQualifiers: [],
                conformance);
            var ports = request.Definition.Ports.Select(port => new RectangularSymbolPort(
                port.Id.Value,
                port.DisplayName,
                port.Direction,
                port.Width)).ToArray();
            var draft = RectangularSymbolGeometryBuilder.Build(
                layoutRequest,
                ports,
                textMeasurer,
                cancellationToken);
            var semanticDigest = GeometryRequestFingerprint.CircuitContract(request.Definition);
            var key = new GeometryPlanKeyV1(
                "logiclab.teachingmixed.circuit-definition",
                "2.0.0",
                semanticDigest,
                SymbolVariantCatalog.RectangularId,
                GeometryRequestFingerprint.Compute(request),
                request.Facing,
                request.IsReflected,
                request.Profile.IndicationConvention,
                request.LocaleId,
                request.BaseDirection,
                request.MetricSet.Id,
                request.MetricSet.Version,
                request.MetricSet.Fingerprint,
                request.FontFingerprint);
            return Publish(draft, key, request, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Cancelled();
        }
        catch (LayoutInvalidException exception)
        {
            return Invalid(PresentationDiagnosticsV1.ConstraintUnsatisfied(exception.Constraint));
        }
        catch (OverflowException)
        {
            return Invalid(PresentationDiagnosticsV1.ConstraintUnsatisfied(
                LayoutConstraintV1.CoordinateRange));
        }
        catch (Exception exception) when (!PresentationExceptionClassifier.IsFatal(exception))
        {
            return InternalDefect();
        }
    }

    private static GeometryPlanRejectedV1? ValidateEnvironment(
        SymbolRequestV1 request,
        ISymbolTextMeasurerV1 textMeasurer)
    {
        if (!SymbolProfileRegistry.IsRegistered(request.Profile))
        {
            return Invalid(PresentationDiagnosticsV1.VariantUnresolved(
                request.Profile.Id,
                request.SymbolVariantId ?? "default"));
        }

        var actualFontFingerprint = TextMeasurementBoundary.FontFingerprint(textMeasurer);
        if (actualFontFingerprint != request.FontFingerprint)
        {
            return Invalid(PresentationDiagnosticsV1.FontFingerprintMismatch(
                request.FontFingerprint,
                actualFontFingerprint));
        }

        var actualMetricSet = TextMeasurementBoundary.MetricSet(textMeasurer);
        if (actualMetricSet != request.MetricSet)
        {
            return Invalid(PresentationDiagnosticsV1.MetricFingerprintMismatch(
                request.MetricSet.Fingerprint,
                actualMetricSet.Fingerprint));
        }

        return null;
    }

    private static GeometryPlanSucceededV1 Publish(
        GeometryPlanDraft draft,
        GeometryPlanKeyV1 key,
        SymbolRequestV1 request,
        CancellationToken cancellationToken)
    {
        var transformed = GeometryPlanTransform.Apply(
            draft,
            request.Facing,
            request.IsReflected);
        cancellationToken.ThrowIfCancellationRequested();
        var plan = new GeometryPlanV1(
            key,
            transformed.Bounds,
            transformed.Operations,
            transformed.PortAnchors,
            transformed.HitRegions,
            transformed.AccessibilityNodes,
            transformed.Conformance);
        cancellationToken.ThrowIfCancellationRequested();
        return new GeometryPlanSucceededV1(plan);
    }

    private static GeometryPlanRejectedV1 VariantUnresolved(
        ComponentSymbolRequestV1 request) => Invalid(
            PresentationDiagnosticsV1.VariantUnresolved(
                request.Profile.Id,
                request.SymbolVariantId ?? request.Contract.Key.ContractId));

    private static ConformanceEvidenceV1 Conformance(
        ResolvedRectangularSymbolDefinition definition,
        RectangularSymbolDynamicInputQualifier[] dynamicInputQualifiers,
        RectangularSymbolActiveLowInputQualifier[] inputQualifiers,
        IndicationConvention indicationConvention,
        SymbolFacingV1 facing)
    {
        string[] qualifierClauses = inputQualifiers.Length == 0
            ? []
            : indicationConvention switch
            {
                IndicationConvention.Negation => ["3.1.1", "3.1-1"],
                IndicationConvention.DirectPolarity when facing == SymbolFacingV1.West =>
                    ["3.1.1", "3.1-5"],
                IndicationConvention.DirectPolarity => ["3.1.1", "3.1-4"],
                _ => throw new LayoutInvalidException(LayoutConstraintV1.IndicationConvention),
            };
        string[] dynamicClauses = dynamicInputQualifiers.Length == 0
            ? []
            : dynamicInputQualifiers.Any(qualifier => qualifier.IsFallingEdge)
                ? indicationConvention == IndicationConvention.Negation
                    ? ["3.1-9", "3.1-10"]
                    : ["3.1-9", "3.1-11"]
                : ["3.1-9"];
        string[] outputQualifierClauses = definition.PortFunctions.Any(
            function => function.IsComplementedOutput)
                ? indicationConvention switch
                {
                    IndicationConvention.Negation => ["3.1.1", "3.1-2"],
                    IndicationConvention.DirectPolarity when facing == SymbolFacingV1.West =>
                        ["3.1.1", "3.1-7"],
                    IndicationConvention.DirectPolarity => ["3.1.1", "3.1-6"],
                    _ => throw new LayoutInvalidException(
                        LayoutConstraintV1.IndicationConvention),
                }
                : [];
        var clauses = definition.StandardClauses
            .Concat(dynamicClauses)
            .Concat(qualifierClauses)
            .Concat(outputQualifierClauses)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return new ConformanceEvidenceV1(
            definition.Claim,
            [new StandardReferenceV1("IEEE-91A", "1991", clauses)],
            definition.Deviations,
            AnnexAStatusV1.NotEvaluated);
    }

    private static ConformanceEvidenceV1 ApplyAggregatePortConformance(
        ConformanceEvidenceV1 conformance,
        IEnumerable<(string Id, uint Width)> ports)
    {
        var aggregatePortIds = ports
            .Where(port => port.Width > 1)
            .Select(port => port.Id)
            .ToArray();
        if (aggregatePortIds.Length == 0)
        {
            return conformance;
        }

        var deviations = conformance.Deviations
            .Where(deviation =>
                deviation.DeviationCode != "teachingmixed-aggregate-multibit-port")
            .Append(new ConformanceDeviationV1(
                "teachingmixed-aggregate-multibit-port",
                aggregatePortIds))
            .ToArray();
        return new ConformanceEvidenceV1(
            ConformanceClaimV1.TeachingExtension,
            conformance.StandardReferences,
            deviations,
            conformance.AnnexA);
    }

    private static RectangularSymbolActiveLowInputQualifier[] ActiveLowQualifiers(
        IReadOnlyList<ComponentParameterBinding> parameters,
        IReadOnlyList<ResolvedComponentPortSchema> ports)
    {
        if (!parameters.Any(parameter =>
                parameter.ParameterId == "enablePolarity"
                && parameter.Value is ChoiceParameterValue { Value: "activeLow" }))
        {
            return [];
        }

        var qualifiers = ports
            .Where(port => port.Id == "EN" && port.Direction == PortDirection.Input)
            .Select(port => new RectangularSymbolActiveLowInputQualifier(port.Id))
            .ToArray();
        if (qualifiers.Length == 0)
        {
            throw new LayoutInvalidException(LayoutConstraintV1.Request);
        }

        return qualifiers;
    }

    private static RectangularSymbolDynamicInputQualifier[] DynamicInputQualifiers(
        IReadOnlyList<ComponentParameterBinding> parameters,
        IReadOnlyList<ResolvedComponentPortSchema> ports)
    {
        var clock = ports.SingleOrDefault(port =>
            port.Id == "CLK" && port.Direction == PortDirection.Input);
        var edgeParameter = parameters.SingleOrDefault(parameter =>
            parameter.ParameterId == "edge");
        if (edgeParameter is null)
        {
            return clock is null
                ? []
                : [new RectangularSymbolDynamicInputQualifier(clock.Id, false)];
        }

        if (edgeParameter.Value is not ChoiceParameterValue edge
            || edge.Value is not ("rising" or "falling"))
        {
            throw new LayoutInvalidException(LayoutConstraintV1.ParameterKind);
        }

        if (clock is null)
        {
            throw new LayoutInvalidException(LayoutConstraintV1.Request);
        }

        return [new RectangularSymbolDynamicInputQualifier(clock.Id, edge.Value == "falling")];
    }

    private static RectangularSymbolThreeStateOutputQualifier[] ThreeStateOutputQualifiers(
        string contractId,
        IReadOnlyList<ResolvedComponentPortSchema> ports)
    {
        if (contractId != "logic.tristate")
        {
            return [];
        }

        var qualifiers = ports
            .Where(port => port.Id == "Q" && port.Direction == PortDirection.Output)
            .Select(port => new RectangularSymbolThreeStateOutputQualifier(port.Id))
            .ToArray();
        if (qualifiers.Length != 1)
        {
            throw new LayoutInvalidException(LayoutConstraintV1.Request);
        }

        return qualifiers;
    }

    private static void ValidateBasicPorts(
        ReadOnlyCollection<ResolvedComponentPortSchema> ports)
    {
        var inputCount = ports.Count(port => port.Direction == PortDirection.Input);
        var outputCount = ports.Count(port => port.Direction == PortDirection.Output);
        if (inputCount == 0
            || outputCount != 1
            || inputCount + outputCount != ports.Count)
        {
            throw new LayoutInvalidException(LayoutConstraintV1.BasicPortContract);
        }
    }

    private static GeometryPlanRejectedV1 Invalid(LayoutDiagnosticV1 diagnostic) => new(
        LayoutRejectionReasonV1.LayoutInvalid,
        [diagnostic]);

    private static GeometryPlanRejectedV1 Cancelled() => new(
        LayoutRejectionReasonV1.LayoutCancelled,
        []);

    private static GeometryPlanRejectedV1 InternalDefect() => new(
        LayoutRejectionReasonV1.LayoutInternalDefect,
        [PresentationDiagnosticsV1.InternalInvariant()]);

}

internal sealed class LayoutInvalidException(LayoutConstraintV1 constraint) :
    Exception("A Geometry Plan request violated a closed layout rule.")
{
    public LayoutConstraintV1 Constraint { get; } = constraint;
}

internal static class GeometryRequestFingerprint
{
    public static string Compute(
        ComponentSymbolRequestV1 request,
        IReadOnlyList<ResolvedComponentPortSchema> ports)
    {
        var canonical = new StringBuilder();
        AppendParameters(canonical, request.Parameters);
        foreach (var port in ports)
        {
            canonical.Append(port.Id)
                .Append(':')
                .Append(port.Direction)
                .Append(':')
                .Append(port.Width.ToString(CultureInfo.InvariantCulture))
                .Append('\n');
        }

        return Digest(canonical);
    }

    public static string Compute(CircuitDefinitionSymbolRequestV1 request)
    {
        var canonical = new StringBuilder();
        canonical.Append(request.DisplayName).Append('\n');
        AppendPorts(canonical, request.Definition.Ports.Select(port => (
            port.Id.Value,
            port.Direction,
            port.Width,
            port.DisplayName)));
        return Digest(canonical);
    }

    public static string CircuitContract(CircuitDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var canonical = new StringBuilder();
        AppendPorts(canonical, definition.Ports.Select(port => (
            port.Id.Value,
            port.Direction,
            port.Width,
            port.DisplayName)));
        return Digest(canonical);
    }

    private static void AppendParameters(
        StringBuilder canonical,
        IReadOnlyList<ComponentParameterBinding> parameters)
    {
        foreach (var parameter in parameters
            .OrderBy(parameter => parameter.ParameterId, StringComparer.Ordinal))
        {
            canonical.Append(parameter.ParameterId)
                .Append('=')
                .Append(ParameterValue(parameter.Value))
                .Append('\n');
        }
    }

    private static void AppendPorts(
        StringBuilder canonical,
        IEnumerable<(string Id, PortDirection Direction, uint Width, string DisplayName)> ports)
    {
        foreach (var port in ports)
        {
            canonical.Append(port.Id)
                .Append(':')
                .Append(port.DisplayName)
                .Append(':')
                .Append(port.Direction)
                .Append(':')
                .Append(port.Width.ToString(CultureInfo.InvariantCulture))
                .Append('\n');
        }
    }

    private static string Digest(StringBuilder canonical) => Convert.ToHexStringLower(
        SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));

    private static string ParameterValue(ComponentParameterValue value)
    {
        return value switch
        {
            Unsigned32ParameterValue unsigned32 => string.Concat(
                "u32:",
                unsigned32.Value.ToString(CultureInfo.InvariantCulture)),
            Unsigned64ParameterValue unsigned64 => string.Concat(
                "u64:",
                unsigned64.Value.ToString(CultureInfo.InvariantCulture)),
            ChoiceParameterValue choice => string.Concat("choice:", choice.Value),
            MemoryImageParameterValue memoryImage => string.Concat(
                "memory:",
                memoryImage.MemoryImageId.Value),
            LogicVectorParameterValue vector => string.Concat(
                "logic:",
                string.Join(',', vector.Values.Select(item => item.ToString()))),
            SlicesParameterValue slices => string.Concat(
                "slices:",
                string.Join(',', slices.Values.Select(slice => string.Concat(
                    slice.Offset.ToString(CultureInfo.InvariantCulture),
                    '+',
                    slice.Length.ToString(CultureInfo.InvariantCulture))))),
            WidthsParameterValue widths => string.Concat(
                "widths:",
                string.Join(',', widths.Values.Select(width =>
                    width.ToString(CultureInfo.InvariantCulture)))),
            _ => throw new LayoutInvalidException(LayoutConstraintV1.ParameterKind),
        };
    }
}
