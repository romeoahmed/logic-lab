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
            if (!IsSupportedProfile(request.Profile))
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
                catch (OperationCanceledException exception)
                    when (IsCooperativeCancellation(exception, cancellationToken))
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
        catch (OperationCanceledException exception)
            when (IsCooperativeCancellation(exception, cancellationToken))
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

    private static bool IsSupportedProfile(SymbolProfileReference profile) =>
        string.Equals(profile.Id, "TeachingMixed", StringComparison.Ordinal)
        && string.Equals(profile.Version, "1.0.0", StringComparison.Ordinal)
        && Enum.IsDefined(profile.IndicationConvention);

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

    private static bool IsCooperativeCancellation(
        OperationCanceledException exception,
        CancellationToken cancellationToken) =>
        cancellationToken.IsCancellationRequested
        && exception.CancellationToken == cancellationToken;

    private static bool IsFatal(Exception exception) =>
        exception is OutOfMemoryException
            or StackOverflowException
            or AccessViolationException
            or AppDomainUnloadedException
            or BadImageFormatException;
}

internal sealed class LayoutInvalidException : Exception
{
    public LayoutInvalidException(LayoutConstraintV1 constraint)
        : base("A Geometry Plan request violated a closed layout rule.")
    {
        Constraint = constraint;
    }

    public LayoutConstraintV1 Constraint { get; }
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
            _ => throw new LayoutInvalidException(LayoutConstraintV1.ParameterKind),
        };
    }
}
