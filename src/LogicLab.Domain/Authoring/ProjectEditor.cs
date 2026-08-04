using System.Text;
using LogicLab.Domain.Components;

namespace LogicLab.Domain.Authoring;

public static partial class ProjectEditor
{
    public static ProjectGenesisOutcome Begin(ProjectSeed seed)
    {
        ArgumentNullException.ThrowIfNull(seed);

        return seed switch
        {
            NewProjectSeed newProjectSeed => BeginNewProject(newProjectSeed),
            _ => throw new InvalidOperationException("The Project Seed variant is undefined."),
        };
    }

    public static EditOutcome Apply(ProjectRevision revision, EditIntent intent)
    {
        ArgumentNullException.ThrowIfNull(revision);
        ArgumentNullException.ThrowIfNull(intent);

        return intent switch
        {
            CreateCircuitDefinitionIntent createDefinition =>
                ApplyCreateDefinition(revision, createDefinition),
            SetEntryCircuitDefinitionIntent setEntry =>
                ApplySetEntryDefinition(revision, setEntry),
            PlaceComponentInstanceIntent place => ApplyPlace(revision, place),
            ConnectTerminalsIntent connect => ApplyConnectTopology(revision, connect),
            MergeNetsIntent merge => ApplyMergeNets(revision, merge),
            SplitNetIntent split => ApplySplitNet(revision, split),
            AddJunctionIntent addJunction => ApplyAddJunction(revision, addJunction),
            RemoveJunctionIntent removeJunction =>
                ApplyRemoveJunction(revision, removeJunction),
            AddWireGeometryIntent addWireGeometry =>
                ApplyAddWireGeometry(revision, addWireGeometry),
            SetWireGeometryIntent setWireGeometry =>
                ApplySetWireGeometry(revision, setWireGeometry),
            RemoveWireGeometryIntent removeWireGeometry =>
                ApplyRemoveWireGeometry(revision, removeWireGeometry),
            MoveComponentInstancesIntent move => ApplyMove(revision, move),
            RenameCircuitDefinitionIntent renameDefinition =>
                ApplyRenameDefinition(revision, renameDefinition),
            ChangePublicPortContractIntent changePorts =>
                ApplyChangePublicPortContract(revision, changePorts),
            MoveDefinitionPortsIntent movePorts =>
                ApplyMoveDefinitionPorts(revision, movePorts),
            RemoveCircuitDefinitionIntent removeDefinition =>
                ApplyRemoveDefinition(revision, removeDefinition),
            RenameComponentInstanceIntent renameInstance =>
                ApplyRenameInstance(revision, renameInstance),
            SetInstanceParametersIntent setParameters =>
                ApplySetInstanceParameters(revision, setParameters),
            ChangeInstanceContractIntent changeContract =>
                ApplyChangeInstanceContract(revision, changeContract),
            RemoveComponentInstancesIntent removeInstances =>
                ApplyRemoveInstances(revision, removeInstances),
            CreateMemoryImageIntent createImage =>
                ApplyCreateMemoryImage(revision, createImage),
            ReplaceMemoryImageIntent replaceImage =>
                ApplyReplaceMemoryImage(revision, replaceImage),
            RemoveMemoryImageIntent removeImage =>
                ApplyRemoveMemoryImage(revision, removeImage),
            SetSymbolProfileIntent setProfile =>
                ApplySetSymbolProfile(revision, setProfile),
            SetSymbolVariantIntent setVariant =>
                ApplySetSymbolVariant(revision, setVariant),
            CreateAnnotationIntent createAnnotation =>
                ApplyCreateAnnotation(revision, createAnnotation),
            ChangeAnnotationIntent changeAnnotation =>
                ApplyChangeAnnotation(revision, changeAnnotation),
            MoveAnnotationsIntent moveAnnotations =>
                ApplyMoveAnnotations(revision, moveAnnotations),
            RemoveAnnotationIntent removeAnnotation =>
                ApplyRemoveAnnotation(revision, removeAnnotation),
            _ => throw new InvalidOperationException("The Edit Intent variant is undefined."),
        };
    }

    private static EditOutcome ApplyCreateDefinition(
        ProjectRevision revision,
        CreateCircuitDefinitionIntent intent)
    {
        var diagnostics = new List<AuthoringDiagnostic>();
        ValidateDisplayText(intent.DisplayName, "displayName", diagnostics);
        foreach (var declaration in intent.Ports)
        {
            ValidateDisplayText(declaration.DisplayName, "portDisplayName", diagnostics);
            if (!Enum.IsDefined(declaration.Direction))
            {
                diagnostics.Add(MissingReference("portDirection"));
            }

            if (declaration.Width == 0)
            {
                diagnostics.Add(InvalidWidth(declaration.Width));
            }

            if (!Enum.IsDefined(declaration.Placement.Facing))
            {
                diagnostics.Add(InvalidCoordinate("definitionPortPlacement", "facing"));
            }
        }

        if (diagnostics.Count != 0)
        {
            return new EditRejected(diagnostics.ToArray());
        }

        var definitionId = CircuitDefinitionId.Create();
        var ports = intent.Ports.Select(declaration => new DefinitionPort(
            DefinitionPortId.Create(),
            declaration.DisplayName,
            declaration.Direction,
            declaration.Width,
            declaration.Placement)).ToArray();
        var definition = new CircuitDefinition(
            definitionId,
            intent.DisplayName,
            ports,
            [],
            [],
            [],
            []);
        var document = revision.Document.AddCircuitDefinition(definition);
        var changedSources = ports
            .Select(port => (AuthoredSourceIdentity)new DefinitionPortSourceIdentity(
                definitionId,
                port.Id))
            .Prepend(new CircuitRootSourceIdentity(definitionId))
            .ToArray();
        return Commit(revision, document, changedSources);
    }

    private static EditOutcome ApplySetEntryDefinition(
        ProjectRevision revision,
        SetEntryCircuitDefinitionIntent intent)
    {
        ArgumentNullException.ThrowIfNull(intent.CircuitDefinitionId);
        if (revision.Document.FindCircuitDefinition(intent.CircuitDefinitionId) is null)
        {
            return Reject(MissingReference("circuitDefinition"));
        }

        if (revision.Document.EntryCircuitDefinitionId == intent.CircuitDefinitionId)
        {
            return new EditCommitted(revision, [], []);
        }

        var document = revision.Document.WithEntryCircuitDefinition(
            intent.CircuitDefinitionId);
        return Commit(
            revision,
            document,
            [new ProjectRootSourceIdentity(document.ProjectId)]);
    }

    private static ProjectGenesisOutcome BeginNewProject(NewProjectSeed seed)
    {
        var diagnostics = new List<AuthoringDiagnostic>();
        ValidateDisplayText(seed.DisplayName, "displayName", diagnostics);
        ValidateDisplayText(
            seed.EntryCircuitDefinitionDisplayName,
            "entryCircuitDefinitionDisplayName",
            diagnostics);

        if (seed.LibrarySnapshot is null)
        {
            diagnostics.Add(new AuthoringDiagnostic(
                "authoring_missing_reference",
                [
                    new AuthoringDiagnosticArgument(
                        "referenceKind",
                        new StableTokenDiagnosticValue("librarySnapshot")),
                ]));
        }

        ValidateSymbolProfile(seed.SymbolProfile, diagnostics);

        if (diagnostics.Count != 0)
        {
            return new ProjectGenesisRejected(diagnostics.ToArray());
        }

        var projectId = ProjectId.Create();
        var circuitDefinitionId = CircuitDefinitionId.Create();
        var definition = new CircuitDefinition(
            circuitDefinitionId,
            seed.EntryCircuitDefinitionDisplayName,
            [],
            [],
            [],
            [],
            []);
        var document = new ProjectDocument(
            projectId,
            seed.DisplayName,
            seed.LibrarySnapshot!,
            seed.SymbolProfile!,
            circuitDefinitionId,
            [definition]);
        var revision = new ProjectRevision(ProjectRevisionId.Create(), document);

        return new ProjectGenesisCommitted(
            revision,
            [
                new ProjectRootSourceIdentity(projectId),
                new CircuitRootSourceIdentity(circuitDefinitionId),
            ]);
    }

    private static EditOutcome ApplyPlace(
        ProjectRevision revision,
        PlaceComponentInstanceIntent intent)
    {
        var definition = revision.Document.FindCircuitDefinition(intent.CircuitDefinitionId);
        if (definition is null)
        {
            return Reject(MissingReference("circuitDefinition"));
        }

        var diagnostics = new List<AuthoringDiagnostic>();
        if (intent.DisplayName is not null)
        {
            ValidateDisplayText(intent.DisplayName, "displayName", diagnostics);
        }

        if (!Enum.IsDefined(intent.Placement.QuarterTurnsClockwise))
        {
            diagnostics.Add(InvalidCoordinate("placement", "orientation"));
        }

        switch (intent.Target)
        {
            case LibraryComponentTarget library:
                var schema = revision.Document.LibrarySnapshot.ResolveContract(
                    library.ContractKey);
                if (schema is null)
                {
                    diagnostics.Add(MissingReference("componentContract"));
                }
                else
                {
                    diagnostics.AddRange(ComponentParameterValidator.Validate(
                        library.ContractKey,
                        schema,
                        intent.Parameters,
                        document: revision.Document));
                }

                break;
            case CircuitDefinitionComponentTarget definitionTarget:
                if (revision.Document.FindCircuitDefinition(
                        definitionTarget.CircuitDefinitionId) is null)
                {
                    diagnostics.Add(MissingReference("circuitDefinitionTarget"));
                }

                if (intent.Parameters.Count != 0)
                {
                    diagnostics.Add(new AuthoringDiagnostic(
                        "authoring_invalid_parameter",
                        [
                            new AuthoringDiagnosticArgument(
                                "contractKey",
                                new ContractKeyDiagnosticValue(new ComponentContractKey(
                                    "logiclab.project",
                                    definitionTarget.CircuitDefinitionId.Value))),
                            new AuthoringDiagnosticArgument(
                                "parameterId",
                                new StableTokenDiagnosticValue("unexpected")),
                            new AuthoringDiagnosticArgument(
                                "rule",
                                new StableTokenDiagnosticValue("definitionParametersEmpty")),
                        ]));
                }

                break;
            default:
                throw new InvalidOperationException(
                    "The Component Target variant is undefined.");
        }

        if (diagnostics.Count != 0)
        {
            return new EditRejected(diagnostics.ToArray());
        }

        var instance = new ComponentInstance(
            ComponentInstanceId.Create(),
            intent.Target,
            intent.Parameters.ToArray(),
            intent.Placement,
            intent.DisplayName);
        var updatedDefinition = definition.AddComponentInstance(instance);
        return Commit(
            revision,
            updatedDefinition,
            [new ComponentInstanceSourceIdentity(definition.Id, instance.Id)]);
    }

    private static EditOutcome ApplyMove(
        ProjectRevision revision,
        MoveComponentInstancesIntent intent)
    {
        var definition = revision.Document.FindCircuitDefinition(intent.CircuitDefinitionId);
        if (definition is null)
        {
            return Reject(MissingReference("circuitDefinition"));
        }

        var diagnostics = new List<AuthoringDiagnostic>();
        if (intent.Moves.Count == 0)
        {
            diagnostics.Add(MissingReference("componentInstance"));
        }

        var seenIds = new HashSet<ComponentInstanceId>();
        var replacements = new List<ComponentInstance>(intent.Moves.Count);

        foreach (var move in intent.Moves)
        {
            if (!seenIds.Add(move.ComponentInstanceId))
            {
                diagnostics.Add(DuplicateId("componentInstance"));
                continue;
            }

            var instance = definition.FindComponentInstance(move.ComponentInstanceId);
            if (instance is null)
            {
                diagnostics.Add(MissingReference("componentInstance"));
                continue;
            }

            if (!Enum.IsDefined(move.Placement.QuarterTurnsClockwise))
            {
                diagnostics.Add(InvalidCoordinate("placement", "orientation"));
                continue;
            }

            replacements.Add(instance.WithPlacement(move.Placement));
        }

        if (diagnostics.Count != 0)
        {
            return new EditRejected(diagnostics.ToArray());
        }

        var updatedDefinition = definition.ReplaceComponentInstances(replacements.ToArray());
        var changedSources = replacements
            .Select(instance => (AuthoredSourceIdentity)new ComponentInstanceSourceIdentity(
                definition.Id,
                instance.Id))
            .ToArray();
        return Commit(revision, updatedDefinition, changedSources);
    }

    private static EditCommitted Commit(
        ProjectRevision previousRevision,
        CircuitDefinition updatedDefinition,
        AuthoredSourceIdentity[] changedSources,
        AuthoredSourceIdentity[]? removedSources = null)
    {
        var document = previousRevision.Document.ReplaceCircuitDefinition(updatedDefinition);
        return Commit(previousRevision, document, changedSources, removedSources);
    }

    private static EditCommitted Commit(
        ProjectRevision previousRevision,
        ProjectDocument document,
        AuthoredSourceIdentity[] changedSources,
        AuthoredSourceIdentity[]? removedSources = null)
    {
        var revision = new ProjectRevision(ProjectRevisionId.Create(), document);
        return new EditCommitted(
            revision,
            changedSources,
            removedSources ?? []);
    }

    private static EditRejected Reject(params AuthoringDiagnostic[] diagnostics)
    {
        return new EditRejected(diagnostics);
    }

    private static AuthoringDiagnostic MissingReference(string referenceKind)
    {
        return new AuthoringDiagnostic(
            "authoring_missing_reference",
            [
                new AuthoringDiagnosticArgument(
                    "referenceKind",
                    new StableTokenDiagnosticValue(referenceKind)),
            ]);
    }

    private static AuthoringDiagnostic InvalidCoordinate(string field, string rule)
    {
        return new AuthoringDiagnostic(
            "authoring_invalid_coordinate",
            [
                new AuthoringDiagnosticArgument(
                    "field",
                    new StableTokenDiagnosticValue(field)),
                new AuthoringDiagnosticArgument(
                    "rule",
                    new StableTokenDiagnosticValue(rule)),
            ]);
    }

    private static AuthoringDiagnostic TerminalAlreadyConnected(
        AuthoredTerminalReference terminal)
    {
        return new AuthoringDiagnostic(
            "authoring_terminal_already_connected",
            [],
            TerminalSource(terminal));
    }

    private static AuthoringDiagnostic InvalidWidth(uint actual)
    {
        return new AuthoringDiagnostic(
            "authoring_invalid_width",
            [
                new AuthoringDiagnosticArgument(
                    "actual",
                    new UnsignedDecimalDiagnosticValue(actual)),
            ]);
    }

    private static void ValidateDisplayText(
        string? value,
        string field,
        List<AuthoringDiagnostic> diagnostics)
    {
        var rule = GetDisplayTextRule(value);
        if (rule is null)
        {
            return;
        }

        diagnostics.Add(new AuthoringDiagnostic(
            "authoring_invalid_text",
            [
                new AuthoringDiagnosticArgument(
                    "field",
                    new StableTokenDiagnosticValue(field)),
                new AuthoringDiagnosticArgument(
                    "rule",
                    new StableTokenDiagnosticValue(rule)),
            ]));
    }

    private static string? GetDisplayTextRule(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "nonempty";
        }

        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (character <= '\u001f')
            {
                return "controlCharacter";
            }

            if (char.IsHighSurrogate(character))
            {
                if (index + 1 >= value.Length
                    || !char.IsLowSurrogate(value[index + 1]))
                {
                    return "unicodeScalar";
                }

                index++;
            }
            else if (char.IsLowSurrogate(character))
            {
                return "unicodeScalar";
            }
        }

        return value.IsNormalized(NormalizationForm.FormC)
            ? null
            : "normalizationFormC";
    }

    private static void ValidateSymbolProfile(
        SymbolProfileReference? symbolProfile,
        List<AuthoringDiagnostic> diagnostics)
    {
        if (symbolProfile is not null
            && SymbolProfileCatalog.Contains(symbolProfile))
        {
            return;
        }

        var profileId = "missing";
        var profileVersion = "missing";
        if (symbolProfile is not null)
        {
            profileId = IsStableName(symbolProfile.Id)
                ? symbolProfile.Id
                : "invalid";
            profileVersion = IsStableVersion(symbolProfile.Version)
                ? symbolProfile.Version
                : "invalid";
        }

        diagnostics.Add(new AuthoringDiagnostic(
            "authoring_symbol_profile_unresolved",
            [
                new AuthoringDiagnosticArgument(
                    "profileId",
                    new StableTokenDiagnosticValue(profileId)),
                new AuthoringDiagnosticArgument(
                    "profileVersion",
                    new StableTokenDiagnosticValue(profileVersion)),
            ]));
    }

    private static bool IsStableName(string? value)
    {
        if (value is null or { Length: < 1 or > 96 }
            || !IsAsciiLetter(value[0]))
        {
            return false;
        }

        return value.All(IsStableNameCharacter);
    }

    private static bool IsStableVersion(string? value)
    {
        return value is { Length: >= 1 and <= 64 }
            && IsAsciiLetterOrDigit(value[0])
            && value.All(IsStableNameCharacter);
    }

    private static bool IsStableNameCharacter(char value)
    {
        return IsAsciiLetterOrDigit(value) || value is '.' or '_' or '-';
    }

    private static bool IsAsciiLetterOrDigit(char value)
    {
        return IsAsciiLetter(value) || value is >= '0' and <= '9';
    }

    private static bool IsAsciiLetter(char value)
    {
        return value is >= 'A' and <= 'Z' or >= 'a' and <= 'z';
    }
}
