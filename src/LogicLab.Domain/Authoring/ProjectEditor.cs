using System.Collections.ObjectModel;
using System.Text;
using LogicLab.Domain.Components;

namespace LogicLab.Domain.Authoring;

public static class ProjectEditor
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
            PlaceComponentInstanceIntent place => ApplyPlace(revision, place),
            ConnectTerminalsIntent connect => ApplyConnect(revision, connect),
            MoveComponentInstancesIntent move => ApplyMove(revision, move),
            _ => throw new InvalidOperationException("The Edit Intent variant is undefined."),
        };
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

        var schema = revision.Document.LibrarySnapshot.FindContract(intent.ContractKey);
        if (schema is null)
        {
            diagnostics.Add(MissingReference("componentContract"));
        }
        else
        {
            ValidateParameters(intent.ContractKey, schema, intent.Parameters, diagnostics);
        }

        if (diagnostics.Count != 0)
        {
            return new EditRejected(diagnostics.ToArray());
        }

        var instance = new ComponentInstance(
            ComponentInstanceId.Create(),
            intent.ContractKey,
            intent.Parameters.ToArray(),
            intent.Placement,
            intent.DisplayName);
        var updatedDefinition = definition.AddComponentInstance(instance);
        var document = revision.Document.ReplaceCircuitDefinition(updatedDefinition);
        var committedRevision = new ProjectRevision(ProjectRevisionId.Create(), document);

        return Commit(
            committedRevision,
            [new ComponentInstanceSourceIdentity(definition.Id, instance.Id)]);
    }

    private static EditOutcome ApplyConnect(
        ProjectRevision revision,
        ConnectTerminalsIntent intent)
    {
        if (intent.Terminals.Count < 2)
        {
            return Reject(MissingReference("electricalEndpoint"));
        }

        var circuitDefinitionId = intent.Terminals[0].CircuitDefinitionId;
        var definition = revision.Document.FindCircuitDefinition(circuitDefinitionId);
        if (definition is null)
        {
            return Reject(MissingReference("circuitDefinition"));
        }

        var diagnostics = new List<AuthoringDiagnostic>();
        var widths = new List<uint>(intent.Terminals.Count);
        var seenTerminals = new HashSet<InstanceTerminalReference>();

        foreach (var terminal in intent.Terminals)
        {
            if (terminal.CircuitDefinitionId != circuitDefinitionId)
            {
                diagnostics.Add(MissingReference("terminalScope"));
                continue;
            }

            if (!seenTerminals.Add(terminal))
            {
                diagnostics.Add(new AuthoringDiagnostic(
                    "authoring_terminal_already_connected",
                    [],
                    new InstancePortSourceIdentity(
                        terminal.CircuitDefinitionId,
                        terminal.ComponentInstanceId,
                        terminal.PortId)));
                continue;
            }

            var instance = definition.FindComponentInstance(terminal.ComponentInstanceId);
            if (instance is null)
            {
                diagnostics.Add(MissingReference("componentInstance"));
                continue;
            }

            var schema = revision.Document.LibrarySnapshot.FindContract(instance.ContractKey);
            var port = schema?.Ports.SingleOrDefault(
                candidate => string.Equals(
                    candidate.Id,
                    terminal.PortId,
                    StringComparison.Ordinal));
            if (port is null || !TryGetPortWidth(instance, port, out var width))
            {
                diagnostics.Add(MissingReference("instancePort"));
                continue;
            }

            widths.Add(width);

            if (definition.Nets.Any(net => net.Terminals.Contains(terminal)))
            {
                diagnostics.Add(new AuthoringDiagnostic(
                    "authoring_terminal_already_connected",
                    [],
                    new InstancePortSourceIdentity(
                        terminal.CircuitDefinitionId,
                        terminal.ComponentInstanceId,
                        terminal.PortId)));
            }
        }

        if (diagnostics.Count == 0
            && widths.Skip(1).Any(width => width != widths[0]))
        {
            diagnostics.Add(new AuthoringDiagnostic(
                "authoring_width_mismatch",
                [
                    new AuthoringDiagnosticArgument(
                        "expected",
                        new UnsignedDecimalDiagnosticValue(widths[0])),
                    new AuthoringDiagnosticArgument(
                        "actual",
                        new UnsignedDecimalDiagnosticValue(
                            widths.First(width => width != widths[0]))),
                ]));
        }

        if (diagnostics.Count != 0)
        {
            return new EditRejected(diagnostics.ToArray());
        }

        var net = new Net(NetId.Create(), widths[0], intent.Terminals.ToArray());
        var updatedDefinition = definition.AddNet(net);
        var document = revision.Document.ReplaceCircuitDefinition(updatedDefinition);
        var committedRevision = new ProjectRevision(ProjectRevisionId.Create(), document);
        var changedSources = intent.Terminals
            .Select(terminal => (AuthoredSourceIdentity)new InstancePortSourceIdentity(
                terminal.CircuitDefinitionId,
                terminal.ComponentInstanceId,
                terminal.PortId))
            .Append(new NetSourceIdentity(definition.Id, net.Id))
            .ToArray();

        return Commit(committedRevision, changedSources);
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
                diagnostics.Add(new AuthoringDiagnostic(
                    "authoring_duplicate_id",
                    [
                        new AuthoringDiagnosticArgument(
                            "entityKind",
                            new StableTokenDiagnosticValue("componentInstance")),
                    ]));
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
        var document = revision.Document.ReplaceCircuitDefinition(updatedDefinition);
        var committedRevision = new ProjectRevision(ProjectRevisionId.Create(), document);
        var changedSources = replacements
            .Select(instance => (AuthoredSourceIdentity)new ComponentInstanceSourceIdentity(
                definition.Id,
                instance.Id))
            .ToArray();
        return Commit(committedRevision, changedSources);
    }

    private static void ValidateParameters(
        ComponentContractKey contractKey,
        ComponentContractSchema schema,
        ReadOnlyCollection<ComponentParameterBinding> parameters,
        List<AuthoringDiagnostic> diagnostics)
    {
        var availableCount = Math.Min(schema.Parameters.Count, parameters.Count);
        for (var index = 0; index < availableCount; index++)
        {
            var expected = schema.Parameters[index];
            var actual = parameters[index];

            if (!string.Equals(expected.Id, actual.ParameterId, StringComparison.Ordinal))
            {
                diagnostics.Add(InvalidParameter(
                    contractKey,
                    expected.Id,
                    "parameterOrder"));
                continue;
            }

            ValidateParameterValue(
                contractKey,
                expected,
                actual.Value,
                parameters,
                diagnostics);
        }

        for (var index = availableCount; index < schema.Parameters.Count; index++)
        {
            diagnostics.Add(InvalidParameter(
                contractKey,
                schema.Parameters[index].Id,
                "missingParameter"));
        }

        for (var index = schema.Parameters.Count; index < parameters.Count; index++)
        {
            diagnostics.Add(InvalidParameter(
                contractKey,
                parameters[index].ParameterId,
                "unknownParameter"));
        }
    }

    private static void ValidateParameterValue(
        ComponentContractKey contractKey,
        ComponentParameterSchema schema,
        ComponentParameterValue? value,
        ReadOnlyCollection<ComponentParameterBinding> allParameters,
        List<AuthoringDiagnostic> diagnostics)
    {
        switch (schema.Kind, value)
        {
            case (ComponentParameterKind.PositiveWidth, Unsigned32ParameterValue { Value: > 0 }):
                return;
            case (ComponentParameterKind.PositiveWidth, Unsigned32ParameterValue):
                diagnostics.Add(InvalidParameter(contractKey, schema.Id, "positiveWidth"));
                return;
            case (ComponentParameterKind.PositiveWidth, _):
                diagnostics.Add(InvalidParameter(contractKey, schema.Id, "parameterKind"));
                return;
            case (ComponentParameterKind.Choice, ChoiceParameterValue choice):
                if (!schema.AllowedValues.Contains(choice.Value, StringComparer.Ordinal))
                {
                    diagnostics.Add(InvalidParameter(contractKey, schema.Id, "allowedValue"));
                }

                return;
            case (ComponentParameterKind.LogicVector, LogicVectorParameterValue vector):
                ValidateLogicVector(
                    contractKey,
                    schema,
                    vector,
                    allParameters,
                    diagnostics);
                return;
            default:
                diagnostics.Add(InvalidParameter(contractKey, schema.Id, "parameterKind"));
                return;
        }
    }

    private static void ValidateLogicVector(
        ComponentContractKey contractKey,
        ComponentParameterSchema schema,
        LogicVectorParameterValue vector,
        ReadOnlyCollection<ComponentParameterBinding> allParameters,
        List<AuthoringDiagnostic> diagnostics)
    {
        if (vector.Values.Count == 0
            || vector.Values.Any(value => value is < LogicValue.Zero or > LogicValue.X))
        {
            diagnostics.Add(InvalidParameter(contractKey, schema.Id, "logicVectorValue"));
            return;
        }

        var width = allParameters
            .Where(binding => string.Equals(
                binding.ParameterId,
                schema.WidthParameterId,
                StringComparison.Ordinal))
            .Select(binding => binding.Value)
            .OfType<Unsigned32ParameterValue>()
            .Select(value => value.Value)
            .SingleOrDefault();

        if (width == 0 || vector.Values.Count != width)
        {
            diagnostics.Add(InvalidParameter(contractKey, schema.Id, "vectorWidth"));
        }
    }

    private static bool TryGetPortWidth(
        ComponentInstance instance,
        ComponentPortSchema port,
        out uint width)
    {
        width = instance.Parameters
            .Where(binding => string.Equals(
                binding.ParameterId,
                port.WidthParameterId,
                StringComparison.Ordinal))
            .Select(binding => binding.Value)
            .OfType<Unsigned32ParameterValue>()
            .Select(parameter => parameter.Value)
            .SingleOrDefault();
        return width > 0;
    }

    private static EditCommitted Commit(
        ProjectRevision revision,
        AuthoredSourceIdentity[] changedSources)
    {
        return new EditCommitted(
            revision,
            Canonicalize(changedSources),
            []);
    }

    private static AuthoredSourceIdentity[] Canonicalize(
        IEnumerable<AuthoredSourceIdentity> sources)
    {
        return sources
            .Distinct()
            .OrderBy(source => source, AuthoredSourceIdentityComparer.Instance)
            .ToArray();
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

    private static AuthoringDiagnostic InvalidParameter(
        ComponentContractKey contractKey,
        string parameterId,
        string rule)
    {
        return new AuthoringDiagnostic(
            "authoring_invalid_parameter",
            [
                new AuthoringDiagnosticArgument(
                    "contractKey",
                    new ContractKeyDiagnosticValue(contractKey)),
                new AuthoringDiagnosticArgument(
                    "parameterId",
                    new StableTokenDiagnosticValue(parameterId)),
                new AuthoringDiagnosticArgument(
                    "rule",
                    new StableTokenDiagnosticValue(rule)),
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

    private sealed class AuthoredSourceIdentityComparer
        : IComparer<AuthoredSourceIdentity>
    {
        public static AuthoredSourceIdentityComparer Instance { get; } = new();

        public int Compare(AuthoredSourceIdentity? left, AuthoredSourceIdentity? right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left is null)
            {
                return -1;
            }

            if (right is null)
            {
                return 1;
            }

            var kindComparison = KindOrder(left).CompareTo(KindOrder(right));
            if (kindComparison != 0)
            {
                return kindComparison;
            }

            return (left, right) switch
            {
                (ProjectRootSourceIdentity l, ProjectRootSourceIdentity r) =>
                    string.CompareOrdinal(l.ProjectId.Value, r.ProjectId.Value),
                (CircuitRootSourceIdentity l, CircuitRootSourceIdentity r) =>
                    string.CompareOrdinal(
                        l.CircuitDefinitionId.Value,
                        r.CircuitDefinitionId.Value),
                (ComponentInstanceSourceIdentity l, ComponentInstanceSourceIdentity r) =>
                    CompareCircuitThenEntity(
                        l.CircuitDefinitionId.Value,
                        l.ComponentInstanceId.Value,
                        r.CircuitDefinitionId.Value,
                        r.ComponentInstanceId.Value),
                (InstancePortSourceIdentity l, InstancePortSourceIdentity r) =>
                    CompareInstancePorts(l, r),
                (NetSourceIdentity l, NetSourceIdentity r) =>
                    CompareCircuitThenEntity(
                        l.CircuitDefinitionId.Value,
                        l.NetId.Value,
                        r.CircuitDefinitionId.Value,
                        r.NetId.Value),
                _ => throw new InvalidOperationException(
                    "The Authored Source Identity variant is undefined."),
            };
        }

        private static int KindOrder(AuthoredSourceIdentity identity)
        {
            return identity switch
            {
                ProjectRootSourceIdentity => 0,
                CircuitRootSourceIdentity => 1,
                ComponentInstanceSourceIdentity => 2,
                InstancePortSourceIdentity => 3,
                NetSourceIdentity => 4,
                _ => throw new InvalidOperationException(
                    "The Authored Source Identity variant is undefined."),
            };
        }

        private static int CompareCircuitThenEntity(
            string leftCircuit,
            string leftEntity,
            string rightCircuit,
            string rightEntity)
        {
            var circuitComparison = string.CompareOrdinal(leftCircuit, rightCircuit);
            return circuitComparison != 0
                ? circuitComparison
                : string.CompareOrdinal(leftEntity, rightEntity);
        }

        private static int CompareInstancePorts(
            InstancePortSourceIdentity left,
            InstancePortSourceIdentity right)
        {
            var entityComparison = CompareCircuitThenEntity(
                left.CircuitDefinitionId.Value,
                left.ComponentInstanceId.Value,
                right.CircuitDefinitionId.Value,
                right.ComponentInstanceId.Value);
            return entityComparison != 0
                ? entityComparison
                : string.CompareOrdinal(left.PortId, right.PortId);
        }
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
            && IsStableName(symbolProfile.Id)
            && IsStableVersion(symbolProfile.Version)
            && Enum.IsDefined(symbolProfile.IndicationConvention))
        {
            return;
        }

        diagnostics.Add(new AuthoringDiagnostic(
            "authoring_symbol_profile_unresolved",
            [
                new AuthoringDiagnosticArgument(
                    "profileId",
                    new StableTokenDiagnosticValue(symbolProfile is null
                        ? "missing"
                        : IsStableName(symbolProfile.Id) ? symbolProfile.Id : "invalid")),
                new AuthoringDiagnosticArgument(
                    "profileVersion",
                    new StableTokenDiagnosticValue(symbolProfile is null
                        ? "missing"
                        : IsStableVersion(symbolProfile.Version)
                            ? symbolProfile.Version
                            : "invalid")),
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
