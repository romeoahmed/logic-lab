using LogicLab.Domain.Authoring;

namespace LogicLab.Application.Workspaces;

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
            RenameCircuitDefinitionIntent => budget.TryConsume(1),
            ChangePublicPortContractIntent changePorts =>
                TryAdmitPublicPortChange(changePorts, budget),
            MoveDefinitionPortsIntent movePorts => budget.TryConsume(movePorts.Moves.Count),
            RemoveCircuitDefinitionIntent => budget.TryConsume(1),
            RenameComponentInstanceIntent => budget.TryConsume(1),
            SetInstanceParametersIntent setParameters =>
                TryAdmitParameters(setParameters.Parameters, budget),
            ChangeInstanceContractIntent changeContract => budget.TryConsume(1)
                && TryAdmitParameters(changeContract.Parameters, budget)
                && budget.TryConsume(changeContract.Ports.Count),
            RemoveComponentInstancesIntent removeInstances =>
                budget.TryConsume(removeInstances.ComponentInstanceIds.Count),
            CreateMemoryImageIntent createImage =>
                TryAdmitMemoryImage(createImage.Words, budget),
            ReplaceMemoryImageIntent replaceImage =>
                TryAdmitMemoryImage(replaceImage.Words, budget)
                && TryAdmitParameterMigrations(replaceImage.AffectedInstances, budget),
            RemoveMemoryImageIntent => budget.TryConsume(1),
            SetSymbolProfileIntent setProfile => budget.TryConsume(1)
                && budget.TryConsume(setProfile.Variants.Count),
            SetSymbolVariantIntent => budget.TryConsume(1),
            CreateAnnotationIntent => budget.TryConsume(1),
            ChangeAnnotationIntent => budget.TryConsume(1),
            MoveAnnotationsIntent moveAnnotations =>
                budget.TryConsume(moveAnnotations.Moves.Count),
            RemoveAnnotationIntent => budget.TryConsume(1),
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
                || !budget.TryConsume(definition.WireGeometries.Count)
                || !budget.TryConsume(definition.Annotations.Count))
            {
                return false;
            }
        }

        return budget.TryConsume(document.MemoryImages.Count);
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

            var nestedItemCount = parameter.Value switch
            {
                LogicVectorParameterValue vector => vector.Values.Count,
                SlicesParameterValue slices => slices.Values.Count,
                WidthsParameterValue widths => widths.Values.Count,
                _ => 0,
            };
            if (!budget.TryConsume(nestedItemCount))
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

    private static bool TryAdmitPublicPortChange(
        ChangePublicPortContractIntent intent,
        AuthoringAdmissionBudget budget)
    {
        if (!budget.TryConsume(intent.Ports.Count)
            || !budget.TryConsume(intent.CallSites.Count))
        {
            return false;
        }

        foreach (var callSite in intent.CallSites)
        {
            if (!budget.TryConsume(callSite.Ports.Count))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryAdmitMemoryImage(
        IEnumerable<MemoryImageWord> words,
        AuthoringAdmissionBudget budget)
    {
        if (!budget.TryConsume(1))
        {
            return false;
        }

        foreach (var word in words)
        {
            if (!budget.TryConsume(1)
                || !budget.TryConsume(word.Values.Count))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryAdmitParameterMigrations(
        IEnumerable<InstanceParameterMigration> migrations,
        AuthoringAdmissionBudget budget)
    {
        foreach (var migration in migrations)
        {
            if (!budget.TryConsume(1)
                || !TryAdmitParameters(migration.Parameters, budget))
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

    private sealed class AuthoringAdmissionBudget
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
}
