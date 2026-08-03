using LogicLab.Domain.Authoring;

namespace LogicLab.Application.Workspaces;

internal struct AuthoringAdmissionBudget
{
    private ulong remaining;

    public AuthoringAdmissionBudget(int maximum)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximum);
        remaining = checked((ulong)maximum);
    }

    public bool TryConsume(ulong itemCount)
    {
        if (itemCount > remaining)
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
        ArgumentNullException.ThrowIfNull(intent);
        ArgumentNullException.ThrowIfNull(policy);
        var budget = new AuthoringAdmissionBudget(
            policy.AuthoringCommandItemCountLimit);
        return intent switch
        {
            CreateCircuitDefinitionIntent create => budget.TryConsume(
                checked((ulong)create.Ports.Count)),
            SetEntryCircuitDefinitionIntent => budget.TryConsume(1),
            PlaceComponentInstanceIntent place => TryAdmitParameters(
                place.Parameters,
                ref budget),
            ConnectTerminalsIntent connect => budget.TryConsume(
                    checked((ulong)connect.Terminals.Count))
                && budget.TryConsume(checked((ulong)connect.NewJunctionPositions.Count))
                && TryAdmitRoutes(connect.RouteAdditions, ref budget)
                && TryAdmitReplacements(connect.RouteReplacements, ref budget),
            MergeNetsIntent merge => budget.TryConsume(
                checked((ulong)merge.SourceNetIds.Count)),
            SplitNetIntent split => TryAdmitPartitions(split.Partitions, ref budget),
            AddJunctionIntent add => budget.TryConsume(1)
                && TryAdmitRoutes(add.RouteAdditions, ref budget)
                && TryAdmitReplacements(add.RouteReplacements, ref budget)
                && budget.TryConsume(checked((ulong)add.RouteRemovals.Count)),
            RemoveJunctionIntent remove => TryAdmitRemovalPartitions(
                    remove.ResultingPartitions,
                    ref budget)
                && TryAdmitReplacements(remove.RouteReplacements, ref budget)
                && budget.TryConsume(checked((ulong)remove.RouteRemovals.Count)),
            AddWireGeometryIntent addWire => TryAdmitRoute(addWire.Route, ref budget),
            SetWireGeometryIntent setWire => TryAdmitRoute(setWire.Route, ref budget),
            RemoveWireGeometryIntent => budget.TryConsume(1),
            MoveComponentInstancesIntent move => budget.TryConsume(
                checked((ulong)move.Moves.Count)),
            _ => false,
        };
    }

    public static bool AdmitsDocument(
        ProjectDocument document,
        WorkspacePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(policy);
        if (document.CircuitDefinitions.Count > policy.AuthoringDefinitionCountLimit)
        {
            return false;
        }

        try
        {
            ulong entityCount = 0;
            foreach (var definition in document.CircuitDefinitions)
            {
                entityCount = checked(
                    entityCount
                    + (ulong)definition.Ports.Count
                    + (ulong)definition.ComponentInstances.Count
                    + (ulong)definition.Nets.Count
                    + (ulong)definition.Junctions.Count
                    + (ulong)definition.WireGeometries.Count);
            }

            return entityCount <= checked((ulong)policy.AuthoringEntityCountLimit);
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static bool TryAdmitParameters(
        IEnumerable<ComponentParameterBinding?> parameters,
        ref AuthoringAdmissionBudget budget)
    {
        foreach (var parameter in parameters)
        {
            if (parameter is null || !budget.TryConsume(1))
            {
                return false;
            }

            if (parameter.Value is LogicVectorParameterValue vector
                && !budget.TryConsume(checked((ulong)vector.Values.Count)))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryAdmitPartitions(
        IEnumerable<NetPartition?> partitions,
        ref AuthoringAdmissionBudget budget)
    {
        foreach (var partition in partitions)
        {
            if (partition is null || !TryAdmitPartition(partition, ref budget))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryAdmitPartition(
        NetPartition partition,
        ref AuthoringAdmissionBudget budget)
    {
        return budget.TryConsume(1)
            && budget.TryConsume(checked((ulong)partition.Terminals.Count))
            && budget.TryConsume(checked((ulong)partition.JunctionIds.Count))
            && budget.TryConsume(checked((ulong)partition.WireGeometryIds.Count));
    }

    private static bool TryAdmitRemovalPartitions(
        IEnumerable<JunctionRemovalPartition?> partitions,
        ref AuthoringAdmissionBudget budget)
    {
        foreach (var partition in partitions)
        {
            if (partition is null
                || !budget.TryConsume(1)
                || !TryAdmitPartition(partition.Membership, ref budget)
                || !TryAdmitRoutes(partition.RouteAdditions, ref budget))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryAdmitReplacements(
        IEnumerable<WireGeometryReplacement?> replacements,
        ref AuthoringAdmissionBudget budget)
    {
        foreach (var replacement in replacements)
        {
            if (replacement is null
                || !budget.TryConsume(1)
                || !TryAdmitRoute(replacement.Route, ref budget))
            {
                return false;
            }
        }

        return true;
    }

    internal static bool TryAdmitRoutes(
        IEnumerable<WireRoute?> routes,
        ref AuthoringAdmissionBudget budget)
    {
        foreach (var route in routes)
        {
            if (!TryAdmitRoute(route, ref budget))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryAdmitRoute(
        WireRoute? route,
        ref AuthoringAdmissionBudget budget)
    {
        return route switch
        {
            UnroutedWireRoute => budget.TryConsume(1),
            OrthogonalWireRoute orthogonal => budget.TryConsume(1)
                && budget.TryConsume(checked((ulong)orthogonal.Points.Count)),
            _ => false,
        };
    }
}
