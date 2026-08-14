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
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentOutOfRangeException.ThrowIfZero(maximumPortCount);
        if (cancellationToken.IsCancellationRequested)
        {
            return Cancelled();
        }

        try
        {
            ValidateProfile(request.Profile);
            cancellationToken.ThrowIfCancellationRequested();
            var resolution = request.Contract.ResolvePorts(
                request.Parameters,
                cancellationToken);
            if (!resolution.TryMaterialize(
                    maximumPortCount,
                    out var ports,
                    cancellationToken))
            {
                return Invalid(
                    "layout_port_budget_exceeded",
                    ("maximumPortCount", maximumPortCount.ToString(
                        CultureInfo.InvariantCulture)));
            }

            ValidateBasicPorts(ports);
            var inputCount = ports.Count(port => port.Direction == PortDirection.Input);
            if (!TeachingMixedBasicSymbolRegistry.TryResolve(
                    request.Contract.Key.ContractId,
                    inputCount,
                    request.SymbolVariantId,
                    request.Profile.IndicationConvention,
                    out var definition,
                    out var diagnosticCode))
            {
                return Invalid(
                    diagnosticCode!,
                    ("contractId", request.Contract.Key.ContractId));
            }

            var resolvedDefinition = definition
                ?? throw new InvalidOperationException(
                    "A successful Symbol Definition lookup returned no definition.");
            var draft = BasicGateGeometryBuilder.Build(
                request,
                resolvedDefinition,
                ports,
                cancellationToken);
            var transformed = GeometryPlanTransform.Apply(
                draft,
                request.Facing,
                request.IsReflected);
            cancellationToken.ThrowIfCancellationRequested();

            var key = new GeometryPlanKeyV1(
                resolvedDefinition.Definition.DefinitionId,
                resolvedDefinition.Definition.DefinitionVersion,
                request.Contract.SchemaDigest,
                resolvedDefinition.VariantId,
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
            return Invalid(exception.DiagnosticCode);
        }
        catch (ArgumentException)
        {
            return Invalid("layout_request_invalid");
        }
        catch (OverflowException)
        {
            return Invalid("layout_coordinate_overflow");
        }
        catch (Exception exception) when (!IsFatal(exception))
        {
            return new GeometryPlanRejectedV1(
                LayoutRejectionReasonV1.LayoutInternalDefect,
                [new LayoutDiagnosticV1("layout_internal_defect", [])]);
        }
    }

    private static void ValidateProfile(SymbolProfileReference profile)
    {
        if (!string.Equals(profile.Id, "TeachingMixed", StringComparison.Ordinal)
            || !string.Equals(profile.Version, "1.0.0", StringComparison.Ordinal)
            || !Enum.IsDefined(profile.IndicationConvention))
        {
            throw new LayoutInvalidException("layout_symbol_profile_unknown");
        }
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
            throw new LayoutInvalidException("layout_basic_port_contract_invalid");
        }
    }

    private static GeometryPlanRejectedV1 Invalid(
        string diagnosticCode,
        params (string Name, string Value)[] arguments) => new(
            LayoutRejectionReasonV1.LayoutInvalid,
            [
                new LayoutDiagnosticV1(
                    diagnosticCode,
                    [.. arguments.Select(argument => new LayoutDiagnosticArgumentV1(
                        argument.Name,
                        argument.Value))]),
            ]);

    private static GeometryPlanRejectedV1 Cancelled() => new(
        LayoutRejectionReasonV1.LayoutCancelled,
        []);

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
    public LayoutInvalidException(string diagnosticCode)
        : base("A Geometry Plan request violated a closed layout rule.")
    {
        DiagnosticCode = diagnosticCode;
    }

    public string DiagnosticCode { get; }
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
            _ => throw new LayoutInvalidException("layout_parameter_kind_unsupported"),
        };
    }
}
