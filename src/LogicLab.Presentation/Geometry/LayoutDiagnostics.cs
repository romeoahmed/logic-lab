using System.Collections.ObjectModel;
using LogicLab.Domain.Components;

namespace LogicLab.Presentation.Geometry;

public enum LayoutDiagnosticSeverityV1
{
    Warning,
    Error,
}

public abstract record LayoutDiagnosticValueV1
{
    private protected LayoutDiagnosticValueV1()
    {
    }
}

public sealed record LayoutStableTokenValueV1 : LayoutDiagnosticValueV1
{
    public LayoutStableTokenValueV1(string value)
    {
        if (!PresentationDiagnosticLexemes.IsStableToken(value))
        {
            throw new ArgumentException("The value is not a stable diagnostic token.", nameof(value));
        }

        Value = value;
    }

    public string Value { get; }
}

public sealed record LayoutDigestValueV1 : LayoutDiagnosticValueV1
{
    public LayoutDigestValueV1(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        if (!FontFingerprintV1.IsDigest(value))
        {
            throw new ArgumentException("The value is not a lowercase SHA-256 digest.", nameof(value));
        }

        Value = value;
    }

    public string Value { get; }
}

public sealed record LayoutCorrelationTokenValueV1 : LayoutDiagnosticValueV1
{
    public LayoutCorrelationTokenValueV1(string value)
    {
        if (!PresentationDiagnosticLexemes.IsCorrelationToken(value))
        {
            throw new ArgumentException("The value is not a correlation token.", nameof(value));
        }

        Value = value;
    }

    public string Value { get; }
}

public sealed record LayoutContractKeyValueV1 : LayoutDiagnosticValueV1
{
    public LayoutContractKeyValueV1(ComponentContractKey value)
    {
        if (string.IsNullOrEmpty(value.LibraryId) || string.IsNullOrEmpty(value.ContractId))
        {
            throw new ArgumentException("The value is not a Component Contract Key.", nameof(value));
        }

        Value = value;
    }

    public ComponentContractKey Value { get; }
}

public sealed record LayoutDiagnosticArgumentV1
{
    public LayoutDiagnosticArgumentV1(string name, LayoutDiagnosticValueV1 value)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(value);
        Name = name;
        Value = value;
    }

    public string Name { get; }

    public LayoutDiagnosticValueV1 Value { get; }
}

public sealed class LayoutDiagnosticV1
{
    internal LayoutDiagnosticV1(
        string code,
        LayoutDiagnosticSeverityV1 severity,
        IReadOnlyList<LayoutDiagnosticArgumentV1> arguments)
    {
        ArgumentException.ThrowIfNullOrEmpty(code);
        ArgumentNullException.ThrowIfNull(arguments);
        PresentationDiagnosticSchema.Validate(code, severity, arguments);
        Code = code;
        Severity = severity;
        Arguments = Array.AsReadOnly(arguments.ToArray());
    }

    public string Code { get; }

    public LayoutDiagnosticSeverityV1 Severity { get; }

    public ReadOnlyCollection<LayoutDiagnosticArgumentV1> Arguments { get; }
}

internal enum LayoutConstraintV1
{
    Request,
    PortBudget,
    BasicPortContract,
    CoordinateRange,
    OutlineRecipe,
    IndicationConvention,
    ParameterKind,
}

internal static class PresentationDiagnosticsV1
{
    public static LayoutDiagnosticV1 VariantUnresolved(
        string profileId,
        string variantId) => new(
            PresentationDiagnosticSchema.VariantUnresolved,
            LayoutDiagnosticSeverityV1.Error,
            [
                Stable("profileId", SafeToken(profileId)),
                Stable("variantId", SafeToken(variantId)),
            ]);

    public static LayoutDiagnosticV1 ConstraintUnsatisfied(LayoutConstraintV1 constraint) => new(
        PresentationDiagnosticSchema.ConstraintUnsatisfied,
        LayoutDiagnosticSeverityV1.Error,
        [Stable("constraint", ConstraintToken(constraint))]);

    public static LayoutDiagnosticV1 UnverifiedFallback(ComponentContractKey contractKey) => new(
        PresentationDiagnosticSchema.UnverifiedFallback,
        LayoutDiagnosticSeverityV1.Warning,
        [new LayoutDiagnosticArgumentV1(
            "contractKey",
            new LayoutContractKeyValueV1(contractKey))]);

    public static LayoutDiagnosticV1 FontFingerprintMismatch(
        FontFingerprintV1 expected,
        FontFingerprintV1 actual) => new(
            PresentationDiagnosticSchema.FontFingerprintMismatch,
            LayoutDiagnosticSeverityV1.Error,
            [
                Digest("expected", expected),
                Digest("actual", actual),
            ]);

    public static LayoutDiagnosticV1 MetricFingerprintMismatch(
        string expected,
        string actual) => new(
            PresentationDiagnosticSchema.MetricFingerprintMismatch,
            LayoutDiagnosticSeverityV1.Error,
            [
                Digest("expected", expected),
                Digest("actual", actual),
            ]);

    public static LayoutDiagnosticV1 InternalInvariant() => new(
        PresentationDiagnosticSchema.InternalInvariant,
        LayoutDiagnosticSeverityV1.Error,
        [new LayoutDiagnosticArgumentV1(
            "correlation",
            new LayoutCorrelationTokenValueV1(Guid.CreateVersion7().ToString("N")))]);

    private static LayoutDiagnosticArgumentV1 Stable(string name, string value) =>
        new(name, new LayoutStableTokenValueV1(value));

    private static LayoutDiagnosticArgumentV1 Digest(
        string name,
        FontFingerprintV1 fingerprint) =>
        Digest(name, fingerprint.Digest);

    private static LayoutDiagnosticArgumentV1 Digest(string name, string digest) =>
        new(name, new LayoutDigestValueV1(digest));

    private static string SafeToken(string value) =>
        PresentationDiagnosticLexemes.IsStableToken(value) ? value : "unregistered";

    private static string ConstraintToken(LayoutConstraintV1 constraint) => constraint switch
    {
        LayoutConstraintV1.Request => "request",
        LayoutConstraintV1.PortBudget => "portBudget",
        LayoutConstraintV1.BasicPortContract => "basicPortContract",
        LayoutConstraintV1.CoordinateRange => "coordinateRange",
        LayoutConstraintV1.OutlineRecipe => "outlineRecipe",
        LayoutConstraintV1.IndicationConvention => "indicationConvention",
        LayoutConstraintV1.ParameterKind => "parameterKind",
        _ => throw new ArgumentOutOfRangeException(nameof(constraint)),
    };
}

internal static class PresentationDiagnosticSchema
{
    public const string VariantUnresolved = "presentation_variant_unresolved";
    public const string ConstraintUnsatisfied = "presentation_constraint_unsatisfied";
    public const string UnverifiedFallback = "presentation_unverified_fallback";
    public const string FontFingerprintMismatch = "presentation_font_fingerprint_mismatch";
    public const string MetricFingerprintMismatch = "presentation_metric_fingerprint_mismatch";
    public const string InternalInvariant = "presentation_internal_invariant";

    public static void Validate(
        string code,
        LayoutDiagnosticSeverityV1 severity,
        IReadOnlyList<LayoutDiagnosticArgumentV1> arguments)
    {
        var (expectedSeverity, expectedArguments) = code switch
        {
            VariantUnresolved => (
                LayoutDiagnosticSeverityV1.Error,
                new[] { ("profileId", typeof(LayoutStableTokenValueV1)), ("variantId", typeof(LayoutStableTokenValueV1)) }),
            ConstraintUnsatisfied => (
                LayoutDiagnosticSeverityV1.Error,
                new[] { ("constraint", typeof(LayoutStableTokenValueV1)) }),
            UnverifiedFallback => (
                LayoutDiagnosticSeverityV1.Warning,
                new[] { ("contractKey", typeof(LayoutContractKeyValueV1)) }),
            FontFingerprintMismatch => (
                LayoutDiagnosticSeverityV1.Error,
                new[] { ("expected", typeof(LayoutDigestValueV1)), ("actual", typeof(LayoutDigestValueV1)) }),
            MetricFingerprintMismatch => (
                LayoutDiagnosticSeverityV1.Error,
                new[] { ("expected", typeof(LayoutDigestValueV1)), ("actual", typeof(LayoutDigestValueV1)) }),
            InternalInvariant => (
                LayoutDiagnosticSeverityV1.Error,
                new[] { ("correlation", typeof(LayoutCorrelationTokenValueV1)) }),
            _ => throw new ArgumentException("The Presentation diagnostic code is not registered.", nameof(code)),
        };

        if (severity != expectedSeverity
            || arguments.Count != expectedArguments.Length
            || arguments.Where((argument, index) =>
                argument.Name != expectedArguments[index].Item1
                || argument.Value.GetType() != expectedArguments[index].Item2).Any())
        {
            throw new ArgumentException(
                "The Presentation diagnostic does not match its registered schema.",
                nameof(arguments));
        }
    }
}

internal static class PresentationDiagnosticLexemes
{
    public static bool IsStableToken(string? value) =>
        value is { Length: >= 1 and <= 96 }
        && char.IsAsciiLetterOrDigit(value[0])
        && value.Skip(1).All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-');

    public static bool IsCorrelationToken(string? value) =>
        value is { Length: >= 16 and <= 64 }
        && IsAsciiLowerAlphaNumeric(value[0])
        && value.Skip(1).All(character =>
            IsAsciiLowerAlphaNumeric(character) || character is '_' or '-');

    private static bool IsAsciiLowerAlphaNumeric(char value) =>
        char.IsAsciiLetterLower(value) || char.IsAsciiDigit(value);
}
