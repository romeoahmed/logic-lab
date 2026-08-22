using System.Globalization;
using LogicLab.Presentation.Geometry;

namespace LogicLab.Presentation.TeachingMixed;

internal sealed record RectangularSymbolPortLabel(
    string Text,
    FontRoleV1 FontRole);

internal static class RectangularSymbolPortLabelComposer
{
    public static Dictionary<string, RectangularSymbolPortLabel> Compose(
        IReadOnlyList<RectangularSymbolPort> ports,
        RectangularSymbolDependency[] dependencies,
        RectangularSymbolInputFunctionQualifier[] inputFunctionQualifiers,
        RectangularSymbolPortFunction[] portFunctions)
    {
        if (!ports.Select(port => port.Id).SequenceEqual(
                portFunctions.Select(function => function.PortId),
                StringComparer.Ordinal))
        {
            throw new LayoutInvalidException(LayoutConstraintV1.Request);
        }

        var affectedPorts = new Dictionary<string, List<AffectedRelationship>>(
            StringComparer.Ordinal);
        var affectedInputFunctions = new Dictionary<
            RectangularSymbolInputFunctionKind,
            List<AffectedRelationship>>();
        foreach (var dependency in dependencies)
        {
            foreach (var endpoint in dependency.AffectedEndpoints)
            {
                var relationship = new AffectedRelationship(dependency, endpoint);
                if (endpoint.InputFunctionKind is { } inputFunctionKind)
                {
                    Add(affectedInputFunctions, inputFunctionKind, relationship);
                }
                else
                {
                    Add(affectedPorts, endpoint.PortId, relationship);
                }
            }
        }

        var affectingRelationships = dependencies.ToLookup(
            dependency => dependency.AffectingPortId,
            StringComparer.Ordinal);
        var functionQualifierByKind = inputFunctionQualifiers.ToDictionary(
            qualifier => qualifier.Kind);
        foreach (var (qualifierKind, relationships) in affectedInputFunctions)
        {
            if (!functionQualifierByKind.TryGetValue(qualifierKind, out var qualifier)
                || relationships.Any(relationship =>
                    relationship.Endpoint.PortId != qualifier.PortId))
            {
                throw new LayoutInvalidException(LayoutConstraintV1.Request);
            }
        }

        var functionQualifiers = inputFunctionQualifiers.ToLookup(
            qualifier => qualifier.PortId,
            StringComparer.Ordinal);
        return ports.Zip(portFunctions).ToDictionary(
            pair => pair.First.Id,
            pair => ComposePortLabel(
                pair.Second.Text,
                affectingRelationships[pair.First.Id],
                affectedPorts.GetValueOrDefault(pair.First.Id),
                functionQualifiers[pair.First.Id],
                affectedInputFunctions),
            StringComparer.Ordinal);
    }

    public static string DependencyLabel(
        RectangularSymbolDependencyKind kind,
        RectangularSymbolDependencyIdentifierRange range)
    {
        var label = string.Concat(
            DependencyLetter(kind),
            range.First.ToString(CultureInfo.InvariantCulture));
        return range.First == range.Last
            ? label
            : string.Concat(
                label,
                '/',
                range.Last.ToString(CultureInfo.InvariantCulture));
    }

    public static string WeightLabel(uint first, uint last)
    {
        var firstText = first.ToString(CultureInfo.InvariantCulture);
        return first == last
            ? firstText
            : string.Concat(
                firstText,
                '/',
                last.ToString(CultureInfo.InvariantCulture));
    }

    private static RectangularSymbolPortLabel ComposePortLabel(
        string? functionText,
        IEnumerable<RectangularSymbolDependency> affecting,
        IReadOnlyList<AffectedRelationship>? affected,
        IEnumerable<RectangularSymbolInputFunctionQualifier> functionQualifiers,
        IReadOnlyDictionary<RectangularSymbolInputFunctionKind, List<AffectedRelationship>>
            affectedInputFunctions)
    {
        var affectingDependencies = affecting.ToArray();
        var affectedNotation = AffectedNotation(affected);
        var affectingNotation = AffectingNotation(affectingDependencies);
        var functionLabel = functionText ?? string.Empty;
        var omitFunctionLabel = affectingDependencies.Length > 0
            && affectingDependencies.All(dependency =>
                dependency.Kind == affectingDependencies[0].Kind)
            && IsDependencyPortLabel(functionLabel, affectingDependencies[0].Kind);
        var primaryFunction = string.Concat(
            affectedNotation,
            affectingNotation,
            omitFunctionLabel ? string.Empty : functionLabel);
        var text = string.Join(
            '/',
            new[] { primaryFunction }
                .Concat(functionQualifiers.Select(qualifier => string.Concat(
                    AffectedNotation(affectedInputFunctions.GetValueOrDefault(
                        qualifier.Kind)),
                    qualifier.Text)))
                .Where(label => label.Length > 0));
        return new RectangularSymbolPortLabel(
            text,
            affectedNotation.Length > 0 || affectingNotation.Length > 0
                ? FontRoleV1.Dependency
                : FontRoleV1.PortLabel);
    }

    private static void Add<TKey>(
        Dictionary<TKey, List<AffectedRelationship>> relationshipsByKey,
        TKey key,
        AffectedRelationship relationship)
        where TKey : notnull
    {
        if (!relationshipsByKey.TryGetValue(key, out var relationships))
        {
            relationships = [];
            relationshipsByKey.Add(key, relationships);
        }

        relationships.Add(relationship);
    }

    private static string AffectingNotation(
        IReadOnlyList<RectangularSymbolDependency> dependencies) => string.Join(
            ',',
            dependencies.GroupBy(dependency => dependency.Kind)
                .OrderBy(group => group.Key)
                .Select(FormatAffectingGroup));

    private static string FormatAffectingGroup(
        IGrouping<RectangularSymbolDependencyKind, RectangularSymbolDependency> group)
    {
        var ranges = group.Select(dependency => dependency.IdentifierRange)
            .OrderBy(range => range.First)
            .ThenBy(range => range.Last)
            .ToArray();
        if (ranges.Length == 1)
        {
            return DependencyLabel(group.Key, ranges[0]);
        }

        var lastIdentifier = ranges[0].Last;
        for (var index = 1; index < ranges.Length; index++)
        {
            if (lastIdentifier == uint.MaxValue
                || ranges[index].First != lastIdentifier + 1)
            {
                return string.Join(
                    ',',
                    ranges.Select(range => DependencyLabel(group.Key, range)));
            }

            lastIdentifier = ranges[index].Last;
        }

        return DependencyLabel(
            group.Key,
            new RectangularSymbolDependencyIdentifierRange(
                ranges[0].First,
                lastIdentifier));
    }

    private static string DependencyLetter(RectangularSymbolDependencyKind kind) => kind switch
    {
        RectangularSymbolDependencyKind.And => "G",
        RectangularSymbolDependencyKind.Enable => "EN",
        RectangularSymbolDependencyKind.Control => "C",
        RectangularSymbolDependencyKind.Mode => "M",
        RectangularSymbolDependencyKind.Address => "A",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static string AffectedNotation(
        IReadOnlyList<AffectedRelationship>? relationships) =>
        relationships is null
            ? string.Empty
            : string.Join(
                ',',
                relationships.OrderBy(relationship =>
                        relationship.Endpoint.ApplicationOrder)
                    .Select(AffectedNotation));

    private static string AffectedNotation(AffectedRelationship relationship)
    {
        var notation = relationship.Dependency.Kind == RectangularSymbolDependencyKind.Address
            ? "A"
            : relationship.Dependency.IdentifierRange.First.ToString(
                CultureInfo.InvariantCulture);
        return relationship.Endpoint.IsComplemented
            ? string.Concat('¬', notation)
            : notation;
    }

    private static bool IsDependencyPortLabel(
        string functionLabel,
        RectangularSymbolDependencyKind kind) =>
        functionLabel == DependencyLetter(kind)
        || (functionLabel == "CLK" && kind == RectangularSymbolDependencyKind.Control)
        || (functionLabel == "EN" && kind == RectangularSymbolDependencyKind.Control)
        || (functionLabel == "LOAD" && kind == RectangularSymbolDependencyKind.Mode);

    private readonly record struct AffectedRelationship(
        RectangularSymbolDependency Dependency,
        RectangularSymbolAffectedEndpoint Endpoint);
}
