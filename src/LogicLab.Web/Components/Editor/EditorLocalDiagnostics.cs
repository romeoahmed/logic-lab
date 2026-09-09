using System.Collections.ObjectModel;
using System.Globalization;
using LogicLab.Application.Workspaces;
using LogicLab.Domain.Authoring;
using LogicLab.Presentation.Geometry;
using LogicLab.Web.Scene;

namespace LogicLab.Web.Components.Editor;

/// <summary>Current adapter evidence; it never changes the Workspace Projection Version.</summary>
public sealed class EditorLocalDiagnostics
{
    internal EditorLocalDiagnostics(
        ProjectRevisionId revisionId,
        CircuitDefinitionId? definitionId,
        IReadOnlyList<LayoutDiagnosticV1> presentation,
        IReadOnlyList<EditorBrowserDiagnostic> browser,
        IReadOnlyList<WorkspaceProbeUnresolved>? probeRecovery = null)
    {
        RevisionId = revisionId;
        DefinitionId = definitionId;
        Presentation = Array.AsReadOnly(presentation.ToArray());
        Browser = Array.AsReadOnly(browser.ToArray());
        ProbeRecovery = Array.AsReadOnly(probeRecovery?.ToArray() ?? []);
    }

    public ProjectRevisionId RevisionId { get; }

    public CircuitDefinitionId? DefinitionId { get; }

    public ReadOnlyCollection<LayoutDiagnosticV1> Presentation { get; }

    public ReadOnlyCollection<EditorBrowserDiagnostic> Browser { get; }

    public ReadOnlyCollection<WorkspaceProbeUnresolved> ProbeRecovery { get; }

    internal bool IsEmpty => Presentation.Count == 0 && Browser.Count == 0 && ProbeRecovery.Count == 0;

    internal bool HasSameEvidence(EditorLocalDiagnostics? other) => other is not null
        && RevisionId == other.RevisionId && DefinitionId == other.DefinitionId
        && Presentation.SequenceEqual(other.Presentation) && Browser.SequenceEqual(other.Browser)
        && ProbeRecovery.SequenceEqual(other.ProbeRecovery);
}

public sealed class EditorBrowserDiagnostic
{
    private EditorBrowserDiagnostic(string code, params EditorDiagnosticArgument[] arguments)
    {
        Code = code;
        Arguments = Array.AsReadOnly(arguments);
    }

    public string Code { get; }

    public ReadOnlyCollection<EditorDiagnosticArgument> Arguments { get; }

    internal static EditorBrowserDiagnostic FromFailure(string code, BrowserPolicyEvidenceV1? evidence = null)
    {
        if (evidence is not null)
        {
            return new("web_browser_policy_exhausted",
                new("policyId", evidence.PolicyId), new("policyRevision", evidence.PolicyRevision),
                new("dimension", evidence.DimensionToken), new("observed", evidence.Observed.ToString(CultureInfo.InvariantCulture)));
        }
        if (code is "contextUnavailable" or "contextLost" or "fontUnavailable" or "assetFingerprintMismatch")
        {
            return new("web_renderer_unavailable", new EditorDiagnosticArgument("reason", code));
        }
        var correlation = Guid.CreateVersion7().ToString("N");
        return code is "invalidSnapshot" or "invalidPatch" or "invalidBatch"
            ? new("web_browser_contract_rejected", new("rule", code), new("correlation", correlation))
            : new("web_interop_failure", new EditorDiagnosticArgument("correlation", correlation));
    }
}

public sealed record EditorDiagnosticArgument(string Name, string Value);
