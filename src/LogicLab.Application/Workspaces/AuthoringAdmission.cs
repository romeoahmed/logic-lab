using LogicLab.Domain.Authoring;

namespace LogicLab.Application.Workspaces;

internal sealed record AuthoringAdmissionPolicy
{
    public AuthoringAdmissionPolicy(
        int definitionCountLimit,
        int entityCountLimit,
        int commandItemCountLimit)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(definitionCountLimit);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(entityCountLimit);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(commandItemCountLimit);
        DefinitionCountLimit = definitionCountLimit;
        EntityCountLimit = entityCountLimit;
        CommandItemCountLimit = commandItemCountLimit;
    }

    public int DefinitionCountLimit { get; }

    public int EntityCountLimit { get; }

    public int CommandItemCountLimit { get; }

    public static AuthoringAdmissionPolicy Default { get; } = new(
        definitionCountLimit: 100,
        entityCountLimit: 10_000,
        commandItemCountLimit: 1_000);
}

internal static class AuthoringAdmission
{
    public static bool AdmitsCommand(
        EditIntent intent,
        AuthoringAdmissionPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(intent);
        ArgumentNullException.ThrowIfNull(policy);
        try
        {
            return CountCommandItems(intent) <= checked((ulong)policy.CommandItemCountLimit);
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    public static bool AdmitsDocument(
        ProjectDocument document,
        AuthoringAdmissionPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(policy);
        if (document.CircuitDefinitions.Count > policy.DefinitionCountLimit)
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

            return entityCount <= checked((ulong)policy.EntityCountLimit);
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static ulong CountCommandItems(EditIntent intent)
    {
        return intent switch
        {
            CreateCircuitDefinitionIntent create => checked((ulong)create.Ports.Count),
            SetEntryCircuitDefinitionIntent => 1,
            PlaceComponentInstanceIntent place => CountParameters(place.Parameters),
            ConnectTerminalsIntent connect => checked(
                (ulong)connect.Terminals.Count
                + (ulong)connect.NewJunctionPositions.Count
                + CountRoutes(connect.RouteAdditions)
                + CountReplacements(connect.RouteReplacements)),
            MergeNetsIntent merge => checked((ulong)merge.SourceNetIds.Count),
            SplitNetIntent split => CountPartitions(split.Partitions),
            AddJunctionIntent add => checked(
                1UL
                + CountRoutes(add.RouteAdditions)
                + CountReplacements(add.RouteReplacements)
                + (ulong)add.RouteRemovals.Count),
            RemoveJunctionIntent remove => checked(
                CountRemovalPartitions(remove.ResultingPartitions)
                + CountReplacements(remove.RouteReplacements)
                + (ulong)remove.RouteRemovals.Count),
            AddWireGeometryIntent addWire => CountRoute(addWire.Route),
            SetWireGeometryIntent setWire => CountRoute(setWire.Route),
            RemoveWireGeometryIntent => 1,
            MoveComponentInstancesIntent move => checked((ulong)move.Moves.Count),
            _ => ulong.MaxValue,
        };
    }

    private static ulong CountParameters(IEnumerable<ComponentParameterBinding?> parameters)
    {
        ulong count = 0;
        foreach (var parameter in parameters)
        {
            if (parameter is null)
            {
                return ulong.MaxValue;
            }

            count = checked(count + 1);
            if (parameter.Value is LogicVectorParameterValue vector)
            {
                count = checked(count + (ulong)vector.Values.Count);
            }
        }

        return count;
    }

    private static ulong CountPartitions(IEnumerable<NetPartition?> partitions)
    {
        ulong count = 0;
        foreach (var partition in partitions)
        {
            if (partition is null)
            {
                return ulong.MaxValue;
            }

            count = checked(
                count
                + 1
                + (ulong)partition.Terminals.Count
                + (ulong)partition.JunctionIds.Count
                + (ulong)partition.WireGeometryIds.Count);
        }

        return count;
    }

    private static ulong CountRemovalPartitions(
        IEnumerable<JunctionRemovalPartition?> partitions)
    {
        ulong count = 0;
        foreach (var partition in partitions)
        {
            if (partition is null)
            {
                return ulong.MaxValue;
            }

            count = checked(
                count
                + 1
                + CountPartitions([partition.Membership])
                + CountRoutes(partition.RouteAdditions));
        }

        return count;
    }

    private static ulong CountReplacements(
        IEnumerable<WireGeometryReplacement?> replacements)
    {
        ulong count = 0;
        foreach (var replacement in replacements)
        {
            if (replacement is null)
            {
                return ulong.MaxValue;
            }

            count = checked(count + 1 + CountRoute(replacement.Route));
        }

        return count;
    }

    private static ulong CountRoutes(IEnumerable<WireRoute?> routes)
    {
        ulong count = 0;
        foreach (var route in routes)
        {
            count = checked(count + CountRoute(route));
        }

        return count;
    }

    private static ulong CountRoute(WireRoute? route)
    {
        return route switch
        {
            UnroutedWireRoute => 1,
            OrthogonalWireRoute orthogonal => checked(1UL + (ulong)orthogonal.Points.Count),
            _ => ulong.MaxValue,
        };
    }
}
