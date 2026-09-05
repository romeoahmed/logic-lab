using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;

namespace LogicLab.Engine.Compilation;

public static partial class Compiler
{
    public const string SemanticVersion = "logiclab.compiler.unified-v3";

    public static CompilationOutcome Compile(
        CompilationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var observations = new Dictionary<ProjectScaleDimension, ulong>();
        try
        {
            return CompileCore(request, observations, cancellationToken);
        }
        catch (OperationCanceledException exception)
            when (ExceptionClassifier.IsCooperativeCancellation(
                exception,
                cancellationToken))
        {
            return Reject(
                request,
                CompilationOutcomeReasons.Cancelled,
                [],
                observations);
        }
        catch (Exception exception) when (!ExceptionClassifier.IsFatal(exception))
        {
            var diagnostic = new CompilerDiagnostic(
                "compiler_internal_invariant",
                [
                    new CompilerDiagnosticArgument(
                        "correlation",
                        new CompilerCorrelationTokenValue(
                            Guid.CreateVersion7().ToString("N"))),
                ],
                new CompilerProjectRootLocation(
                    request.ProjectRevision.Document.ProjectId));
            return Reject(
                request,
                CompilationOutcomeReasons.InternalDefect,
                [diagnostic],
                observations);
        }
    }

    private static CompilationOutcome CompileCore(
        CompilationRequest request,
        Dictionary<ProjectScaleDimension, ulong> observations,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var diagnostics = new List<CompilerDiagnostic>();
        ValidateLibrarySnapshot(request, diagnostics);
        var definition = request.ProjectRevision.Document.FindCircuitDefinition(
            request.EntryCircuitDefinitionId);
        if (definition is null)
        {
            diagnostics.Add(new CompilerDiagnostic(
                "compiler_entry_definition_missing",
                [],
                new CompilerProjectRootLocation(
                    request.ProjectRevision.Document.ProjectId)));

            cancellationToken.ThrowIfCancellationRequested();
            return RejectInvalid(request, diagnostics, observations);
        }

        if (diagnostics.Count != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return RejectInvalid(request, diagnostics, observations);
        }

        var policyRejection = ObserveInitialDimensions(
            request,
            observations,
            cancellationToken);
        if (policyRejection is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return policyRejection;
        }

        cancellationToken.ThrowIfCancellationRequested();

        return CompileHierarchy(request, definition, observations, cancellationToken);
    }

    private static void ValidateLibrarySnapshot(
        CompilationRequest request,
        List<CompilerDiagnostic> diagnostics)
    {
        var expected = request.ProjectRevision.Document.LibrarySnapshot;
        var actual = request.LibrarySnapshot;
        var primary = new CompilerProjectRootLocation(
            request.ProjectRevision.Document.ProjectId);

        if (!string.Equals(
                expected.LibraryId,
                actual.LibraryId,
                StringComparison.Ordinal)
            || !string.Equals(
                expected.Version,
                actual.Version,
                StringComparison.Ordinal))
        {
            diagnostics.Add(new CompilerDiagnostic(
                "compiler_library_version_mismatch",
                [
                    new CompilerDiagnosticArgument(
                        "libraryId",
                        new CompilerStableTokenValue(expected.LibraryId)),
                    new CompilerDiagnosticArgument(
                        "expectedVersion",
                        new CompilerStableTokenValue(expected.Version)),
                    new CompilerDiagnosticArgument(
                        "actualVersion",
                        new CompilerStableTokenValue(actual.Version)),
                ],
                primary));
        }

        if (!string.Equals(
                expected.ContentDigest,
                actual.ContentDigest,
                StringComparison.Ordinal))
        {
            diagnostics.Add(new CompilerDiagnostic(
                "compiler_library_digest_mismatch",
                [
                    new CompilerDiagnosticArgument(
                        "libraryId",
                        new CompilerStableTokenValue(expected.LibraryId)),
                    new CompilerDiagnosticArgument(
                        "expected",
                        new CompilerDigestValue(expected.ContentDigest)),
                    new CompilerDiagnosticArgument(
                        "actual",
                        new CompilerDigestValue(actual.ContentDigest)),
                ],
                primary));
        }
    }

    private static CompilationRejected? ObserveInitialDimensions(
        CompilationRequest request,
        Dictionary<ProjectScaleDimension, ulong> observations,
        CancellationToken cancellationToken)
    {
        var document = request.ProjectRevision.Document;
        ulong entityCount = 0;
        foreach (var definition in document.CircuitDefinitions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            entityCount = checked(
                entityCount
                + (ulong)definition.Ports.Count
                + (ulong)definition.ComponentInstances.Count
                + (ulong)definition.Nets.Count
                + (ulong)definition.Junctions.Count
                + (ulong)definition.WireGeometries.Count);
        }

        var dimensions = new[]
        {
            new ObservedProjectScaleDimension(
                ProjectScaleDimension.DefinitionCount,
                checked((ulong)document.CircuitDefinitions.Count)),
            new ObservedProjectScaleDimension(
                ProjectScaleDimension.EntityCount,
                entityCount),
        };

        foreach (var dimension in dimensions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rejection = Observe(
                request,
                dimension.Dimension,
                dimension.Observed,
                observations);
            if (rejection is not null)
            {
                return rejection;
            }
        }

        return null;
    }

    private static CompilationRejected? Observe(
        CompilationRequest request,
        ProjectScaleDimension dimension,
        ulong observed,
        Dictionary<ProjectScaleDimension, ulong> observations,
        bool exceedsMaximum = false)
    {
        observations[dimension] = observed;
        if (!exceedsMaximum && observed <= request.Policy.Maximum(dimension))
        {
            return null;
        }

        var breach = new ObservedProjectScaleDimension(dimension, observed);
        var diagnostic = new CompilerDiagnostic(
            "compiler_policy_exhausted",
            [
                new CompilerDiagnosticArgument(
                    "policyId",
                    new CompilerStableTokenValue(request.Policy.PolicyId)),
                new CompilerDiagnosticArgument(
                    "policyRevision",
                    new CompilerStableTokenValue(request.Policy.PolicyRevision)),
                new CompilerDiagnosticArgument(
                    "dimension",
                    new CompilerStableTokenValue(breach.DimensionToken)),
                new CompilerDiagnosticArgument(
                    "observed",
                    new CompilerUnsignedDecimalValue(observed)),
            ],
            new CompilerProjectRootLocation(
                request.ProjectRevision.Document.ProjectId));
        return Reject(
            request,
            CompilationOutcomeReasons.PolicyExhausted,
            [diagnostic],
            observations,
            breach);
    }

    private static CompilationRejected? ObserveElaboratedSlots(
        CompilationRequest request,
        ulong baseSlotCount,
        IEnumerable<ComponentPortResolution> portResolutions,
        Dictionary<ProjectScaleDimension, ulong> observations,
        CancellationToken cancellationToken)
    {
        var dimension = ProjectScaleDimension.ElaboratedSlotCount;
        var maximum = request.Policy.Maximum(dimension);
        var observed = baseSlotCount;
        foreach (var resolution in portResolutions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!resolution.TryGetPortCount(out var portCount)
                || portCount > ulong.MaxValue - observed)
            {
                var firstExceeded = maximum == ulong.MaxValue
                    ? ulong.MaxValue
                    : maximum + 1;
                return Observe(
                    request,
                    dimension,
                    firstExceeded,
                    observations,
                    exceedsMaximum: true)!;
            }

            observed += portCount;
        }

        return Observe(request, dimension, observed, observations);
    }

    private static CompilationSource Source(
        HierarchyPath path,
        AuthoredSourceIdentity identity)
    {
        return new CompilationSource(identity, path);
    }

    private static CompilerCircuitLocation CircuitLocation(
        HierarchyPath path,
        AuthoredSourceIdentity identity)
    {
        return new CompilerCircuitLocation(Source(path, identity));
    }

    private static CompilationRejected Reject(
        CompilationRequest request,
        string reason,
        CompilerDiagnostic[] diagnostics,
        Dictionary<ProjectScaleDimension, ulong> observations,
        ObservedProjectScaleDimension? breach = null)
    {
        return new CompilationRejected(
            reason,
            diagnostics,
            CreateEvidence(request, observations, breach));
    }

    private static CompilationRejected RejectInvalid(
        CompilationRequest request,
        IEnumerable<CompilerDiagnostic> diagnostics,
        Dictionary<ProjectScaleDimension, ulong> observations)
    {
        return Reject(
            request,
            CompilationOutcomeReasons.Invalid,
            CompilerCanonicalizer.Diagnostics(diagnostics),
            observations);
    }

    private static CompilationEvidence CreateEvidence(
        CompilationRequest request,
        Dictionary<ProjectScaleDimension, ulong> observations,
        ObservedProjectScaleDimension? breach)
    {
        return new CompilationEvidence(
            request.ProjectRevision.RevisionId,
            request.EntryCircuitDefinitionId,
            request.LibrarySnapshot.Fingerprint,
            SemanticVersion,
            new CompilationPolicyReference(
                request.Policy.PolicyId,
                request.Policy.PolicyRevision),
            [.. observations
                .OrderBy(
                    row => ProjectScaleDimensionVocabulary.Token(row.Key),
                    StringComparer.Ordinal)
                .Select(row => new ObservedProjectScaleDimension(row.Key, row.Value))],
            breach);
    }

}
