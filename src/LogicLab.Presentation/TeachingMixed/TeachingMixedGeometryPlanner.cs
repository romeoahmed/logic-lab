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
        BasicSymbolRequestV1 request,
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
            if (!SymbolProfileRegistry.IsRegistered(request.Profile))
            {
                return Invalid(PresentationDiagnosticsV1.VariantUnresolved(
                    request.Profile.Id,
                    request.SymbolVariantId ?? "default"));
            }

            var measuredFontFingerprint = textMeasurer.FontFingerprint;
            if (measuredFontFingerprint != request.FontFingerprint)
            {
                return Invalid(PresentationDiagnosticsV1.FontFingerprintMismatch(
                    request.FontFingerprint,
                    measuredFontFingerprint));
            }

            var measuredMetricSet = textMeasurer.MetricSet;
            if (measuredMetricSet != request.MetricSet)
            {
                return Invalid(PresentationDiagnosticsV1.MetricFingerprintMismatch(
                    request.MetricSet.Fingerprint,
                    measuredMetricSet.Fingerprint));
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

            ValidateBasicPorts(ports);
            var inputCount = ports.Count(port => port.Direction == PortDirection.Input);
            if (!TeachingMixedBasicSymbolRegistry.TryResolve(
                    request.Contract.Key.ContractId,
                    inputCount,
                    request.SymbolVariantId,
                    request.Profile.IndicationConvention,
                    request.Facing,
                    out var definition))
            {
                return Invalid(PresentationDiagnosticsV1.VariantUnresolved(
                    request.Profile.Id,
                    request.SymbolVariantId ?? request.Contract.Key.ContractId));
            }

            SymbolTextMeasurementV1? textMeasurement = null;
            if (definition.Recipe == BasicOutlineRecipe.Rectangle)
            {
                try
                {
                    textMeasurement = textMeasurer.Measure(
                        new SymbolTextMeasurementRequestV1(
                            definition.FunctionText,
                            FontRoleV1.Symbol,
                            TextAlignmentV1.Center,
                            request.MetricSet,
                            request.LocaleId,
                            request.BaseDirection),
                        cancellationToken)
                        ?? throw new InvalidOperationException(
                            "The Symbol Text Measurer returned no measurement.");
                }
                catch (OperationCanceledException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception) when (!IsFatal(exception))
                {
                    return InternalDefect();
                }
            }

            var draft = BasicGateGeometryBuilder.Build(
                request,
                definition,
                ports,
                textMeasurement,
                cancellationToken);
            var transformed = GeometryPlanTransform.Apply(
                draft,
                request.Facing,
                request.IsReflected);
            cancellationToken.ThrowIfCancellationRequested();

            var key = new GeometryPlanKeyV1(
                definition.Definition.DefinitionId,
                definition.Definition.DefinitionVersion,
                request.Contract.SchemaDigest,
                definition.VariantId,
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
        catch (Exception exception) when (!IsFatal(exception))
        {
            return InternalDefect();
        }
    }

    public static GeometryPlanOutcomeV1 Plan(
        ComplexSymbolRequestV1 request,
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
            var environmentFailure = ValidateEnvironment(
                request.Profile,
                request.SymbolVariantId,
                request.MetricSet,
                request.FontFingerprint,
                textMeasurer);
            if (environmentFailure is not null)
            {
                return environmentFailure;
            }

            cancellationToken.ThrowIfCancellationRequested();
            ReadOnlyCollection<ResolvedComponentPortSchema> ports;
            try
            {
                var resolution = request.Contract.ResolvePorts(request.Parameters, cancellationToken);
                if (!resolution.TryMaterialize(maximumPortCount, out ports, cancellationToken))
                {
                    return Invalid(PresentationDiagnosticsV1.ConstraintUnsatisfied(
                        LayoutConstraintV1.PortBudget));
                }
            }
            catch (ArgumentException)
            {
                return Invalid(PresentationDiagnosticsV1.ConstraintUnsatisfied(
                    LayoutConstraintV1.Request));
            }

            if (!TeachingMixedRectangularSymbolRegistry.TryResolve(
                    request.Contract.Key.ContractId,
                    request.Parameters,
                    ports,
                    request.SymbolVariantId,
                    out var definition))
            {
                return Invalid(PresentationDiagnosticsV1.VariantUnresolved(
                    request.Profile.Id,
                    request.SymbolVariantId ?? request.Contract.Key.ContractId));
            }

            var conformance = Conformance(definition);
            var layoutRequest = new RectangularSymbolLayoutRequest(
                definition.FunctionText,
                definition.DeviationCode is null
                    ? FontRoleV1.Symbol
                    : FontRoleV1.ExtensionMark,
                definition.AccessibilityKey,
                definition.Dependencies,
                request.MetricSet,
                request.LocaleId,
                request.BaseDirection,
                request.Profile.IndicationConvention,
                HasActiveLowEnable(request.Parameters),
                request.Contract.Key.ContractId == "logic.tristate",
                conformance);
            var rectangularPorts = ports.Select(port => new RectangularSymbolPort(
                port.Id,
                port.Id,
                port.Direction,
                port.Width)).ToArray();
            var draft = RectangularSymbolGeometryBuilder.Build(
                layoutRequest,
                rectangularPorts,
                textMeasurer,
                cancellationToken);
            var transformed = GeometryPlanTransform.Apply(
                draft,
                request.Facing,
                request.IsReflected);
            cancellationToken.ThrowIfCancellationRequested();
            var key = new GeometryPlanKeyV1(
                definition.DefinitionId,
                definition.DefinitionVersion,
                request.Contract.SchemaDigest,
                definition.VariantId,
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
        catch (Exception exception) when (!IsFatal(exception))
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
            var environmentFailure = ValidateEnvironment(
                request.Profile,
                request.SymbolVariantId,
                request.MetricSet,
                request.FontFingerprint,
                textMeasurer);
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
            var conformance = new ConformanceEvidenceV1(
                ConformanceClaimV1.TeachingExtension,
                standardReferences,
                [new ConformanceDeviationV1(
                    "teachingmixed-user-circuit-definition",
                    [.. request.Definition.Ports.Select(port => port.Id.Value)])],
                AnnexAStatusV1.NotEvaluated);
            var layoutRequest = new RectangularSymbolLayoutRequest(
                request.DisplayName,
                FontRoleV1.ExtensionMark,
                "presentation.symbol.circuit-definition",
                [],
                request.MetricSet,
                request.LocaleId,
                request.BaseDirection,
                request.Profile.IndicationConvention,
                HasActiveLowEnable: false,
                HasThreeStateOutput: false,
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
            var transformed = GeometryPlanTransform.Apply(
                draft,
                request.Facing,
                request.IsReflected);
            cancellationToken.ThrowIfCancellationRequested();
            var semanticDigest = GeometryRequestFingerprint.CircuitContract(request.Definition);
            var key = new GeometryPlanKeyV1(
                "logiclab.teachingmixed.circuit-definition",
                "1.0.0",
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
        catch (Exception exception) when (!IsFatal(exception))
        {
            return InternalDefect();
        }
    }

    private static GeometryPlanRejectedV1? ValidateEnvironment(
        SymbolProfileReference profile,
        string? symbolVariantId,
        SymbolMetricSetV1 metricSet,
        FontFingerprintV1 fontFingerprint,
        ISymbolTextMeasurerV1 textMeasurer)
    {
        if (!SymbolProfileRegistry.IsRegistered(profile))
        {
            return Invalid(PresentationDiagnosticsV1.VariantUnresolved(
                profile.Id,
                symbolVariantId ?? "default"));
        }

        if (textMeasurer.FontFingerprint != fontFingerprint)
        {
            return Invalid(PresentationDiagnosticsV1.FontFingerprintMismatch(
                fontFingerprint,
                textMeasurer.FontFingerprint));
        }

        if (textMeasurer.MetricSet != metricSet)
        {
            return Invalid(PresentationDiagnosticsV1.MetricFingerprintMismatch(
                metricSet.Fingerprint,
                textMeasurer.MetricSet.Fingerprint));
        }

        return null;
    }

    private static ConformanceEvidenceV1 Conformance(
        ResolvedRectangularSymbolDefinition definition) => new(
            definition.Claim,
            [new StandardReferenceV1("IEEE-91A", "1991", definition.StandardClauses)],
            definition.DeviationCode is { } deviationCode
                ? [new ConformanceDeviationV1(deviationCode, [])]
                : [],
            AnnexAStatusV1.NotEvaluated);

    private static bool HasActiveLowEnable(
        IReadOnlyList<ComponentParameterBinding> parameters) => parameters.Any(parameter =>
            parameter.ParameterId == "enablePolarity"
            && parameter.Value is ChoiceParameterValue { Value: "activeLow" });

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

    private static bool IsFatal(Exception exception) =>
        exception is OutOfMemoryException
            or StackOverflowException
            or AccessViolationException
            or AppDomainUnloadedException
            or BadImageFormatException;
}

internal sealed class LayoutInvalidException(LayoutConstraintV1 constraint) :
    Exception("A Geometry Plan request violated a closed layout rule.")
{
    public LayoutConstraintV1 Constraint { get; } = constraint;
}

internal static class GeometryRequestFingerprint
{
    public static string Compute(
        BasicSymbolRequestV1 request,
        IReadOnlyList<ResolvedComponentPortSchema> ports)
    {
        var canonical = new StringBuilder();
        foreach (var parameter in request.Parameters
            .OrderBy(parameter => parameter.ParameterId, StringComparer.Ordinal))
        {
            canonical.Append(parameter.ParameterId)
                .Append('=')
                .Append(ParameterValue(parameter.Value))
                .Append('\n');
        }

        foreach (var port in ports)
        {
            canonical.Append(port.Id)
                .Append(':')
                .Append(port.Direction)
                .Append(':')
                .Append(port.Width.ToString(CultureInfo.InvariantCulture))
                .Append('\n');
        }

        canonical.Append(request.LocaleId)
            .Append('\n')
            .Append(request.BaseDirection);
        return Convert.ToHexStringLower(SHA256.HashData(
            Encoding.UTF8.GetBytes(canonical.ToString())));
    }

    public static string Compute(
        ComplexSymbolRequestV1 request,
        IReadOnlyList<ResolvedComponentPortSchema> ports)
    {
        var canonical = new StringBuilder();
        AppendParameters(canonical, request.Parameters);
        AppendPorts(canonical, ports.Select(port => (
            port.Id,
            port.Direction,
            port.Width,
            port.Id)));
        canonical.Append(request.LocaleId)
            .Append('\n')
            .Append(request.BaseDirection);
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
        canonical.Append(request.LocaleId)
            .Append('\n')
            .Append(request.BaseDirection);
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
