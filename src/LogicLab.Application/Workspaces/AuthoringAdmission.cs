using LogicLab.Domain.Authoring;

namespace LogicLab.Application.Workspaces;

internal sealed class AuthoringAdmissionBudget
{
    private int remaining;

    public AuthoringAdmissionBudget(int maximum)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximum);
        remaining = maximum;
    }

    public bool TryConsume(int itemCount)
    {
        if (itemCount < 0 || itemCount > remaining)
        {
            return false;
        }

        remaining -= itemCount;
        return true;
    }
}

internal static class AuthoringAdmission
{
    public static bool AdmitsCommand(
        EditIntent intent,
        WorkspacePolicy policy)
    {
        var budget = new AuthoringAdmissionBudget(
            policy.AuthoringLimits.CommandItemCount);
        return intent switch
        {
            CreateCircuitDefinitionIntent create => budget.TryConsume(create.Ports.Count),
            SetEntryCircuitDefinitionIntent => budget.TryConsume(1),
            PlaceComponentInstanceIntent place => TryAdmitParameters(place.Parameters, budget),
            ConnectTerminalsIntent connect => budget.TryConsume(connect.Terminals.Count)
                && budget.TryConsume(connect.NewJunctionPositions.Count)
                && TryAdmitRoutes(connect.RouteAdditions, budget)
                && TryAdmitReplacements(connect.RouteReplacements, budget),
            MergeNetsIntent merge => budget.TryConsume(merge.SourceNetIds.Count),
            SplitNetIntent split => TryAdmitPartitions(split.Partitions, budget),
            AddJunctionIntent add => budget.TryConsume(1)
                && TryAdmitRoutes(add.RouteAdditions, budget)
                && TryAdmitReplacements(add.RouteReplacements, budget)
                && budget.TryConsume(add.RouteRemovals.Count),
            RemoveJunctionIntent remove => TryAdmitRemovalPartitions(
                    remove.ResultingPartitions,
                    budget)
                && TryAdmitReplacements(remove.RouteReplacements, budget)
                && budget.TryConsume(remove.RouteRemovals.Count),
            AddWireGeometryIntent addWire => TryAdmitRoute(addWire.Route, budget),
            SetWireGeometryIntent setWire => TryAdmitRoute(setWire.Route, budget),
            RemoveWireGeometryIntent => budget.TryConsume(1),
            MoveComponentInstancesIntent move => budget.TryConsume(move.Moves.Count),
            _ => false,
        };
    }

    public static bool AdmitsDocument(
        ProjectDocument document,
        WorkspacePolicy policy)
    {
        if (document.CircuitDefinitions.Count > policy.AuthoringLimits.DefinitionCount)
        {
            return false;
        }

        var budget = new AuthoringAdmissionBudget(policy.AuthoringLimits.EntityCount);
        foreach (var definition in document.CircuitDefinitions)
        {
            if (!budget.TryConsume(definition.Ports.Count)
                || !budget.TryConsume(definition.ComponentInstances.Count)
                || !budget.TryConsume(definition.Nets.Count)
                || !budget.TryConsume(definition.Junctions.Count)
                || !budget.TryConsume(definition.WireGeometries.Count))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryAdmitParameters(
        IEnumerable<ComponentParameterBinding> parameters,
        AuthoringAdmissionBudget budget)
    {
        foreach (var parameter in parameters)
        {
            if (!budget.TryConsume(1))
            {
                return false;
            }

            if (parameter.Value is LogicVectorParameterValue vector
                && !budget.TryConsume(vector.Values.Count))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryAdmitPartitions(
        IEnumerable<NetPartition> partitions,
        AuthoringAdmissionBudget budget)
    {
        foreach (var partition in partitions)
        {
            if (!TryAdmitPartition(partition, budget))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryAdmitPartition(
        NetPartition partition,
        AuthoringAdmissionBudget budget)
    {
        return budget.TryConsume(1)
            && budget.TryConsume(partition.Terminals.Count)
            && budget.TryConsume(partition.JunctionIds.Count)
            && budget.TryConsume(partition.WireGeometryIds.Count);
    }

    private static bool TryAdmitRemovalPartitions(
        IEnumerable<JunctionRemovalPartition> partitions,
        AuthoringAdmissionBudget budget)
    {
        foreach (var partition in partitions)
        {
            if (!budget.TryConsume(1)
                || !TryAdmitPartition(partition.Membership, budget)
                || !TryAdmitRoutes(partition.RouteAdditions, budget))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryAdmitReplacements(
        IEnumerable<WireGeometryReplacement> replacements,
        AuthoringAdmissionBudget budget)
    {
        foreach (var replacement in replacements)
        {
            if (!budget.TryConsume(1)
                || !TryAdmitRoute(replacement.Route, budget))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryAdmitRoutes(
        IEnumerable<WireRoute> routes,
        AuthoringAdmissionBudget budget)
    {
        foreach (var route in routes)
        {
            if (!TryAdmitRoute(route, budget))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryAdmitRoute(
        WireRoute route,
        AuthoringAdmissionBudget budget)
    {
        return route switch
        {
            UnroutedWireRoute => budget.TryConsume(1),
            OrthogonalWireRoute orthogonal => budget.TryConsume(1)
                && budget.TryConsume(orthogonal.Points.Count),
            _ => false,
        };
    }
}
