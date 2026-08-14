using System.Collections.ObjectModel;
using System.Text;
using FsCheck;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using TUnit.FsCheck;
using static LogicLab.Domain.Tests.ProjectEditorTestContext;

namespace LogicLab.Domain.Tests;

internal sealed class ProjectEditorSequenceTests
{
    [Test, FsCheckProperty(MaxTest = 50)]
    public async Task Apply_GeneratedIntentSequence_PreservesModelAndRevisionInvariants(
        NonEmptyArray<byte> commandSequence)
    {
        var revision = ((ProjectGenesisCommitted)ProjectEditor.Begin(
            new NewProjectSeed(
                "Sequence model",
                LibrarySnapshot.Core,
                TeachingMixedProfile(),
                "Main"))).Revision;
        var model = new EditorModel();
        var violations = new List<string>();

        for (var step = 0; step < commandSequence.Get.Length; step++)
        {
            var command = commandSequence.Get[step];
            if (command % 12 == 11)
            {
                VerifyExpectedRejection(revision, step, violations);
                continue;
            }

            var previous = revision;
            var previousSnapshot = Snapshot(previous.Document);
            var operation = CreateOperation(revision, model, command, step);
            var outcome = ProjectEditor.Apply(revision, operation.Intent);
            if (outcome is not EditCommitted committed)
            {
                var rejected = (EditRejected)outcome;
                violations.Add(
                    $"step {step}: {operation.Name} unexpectedly rejected with " +
                    string.Join(", ", rejected.Diagnostics.Select(item => item.Code)));
                break;
            }

            revision = committed.Revision;
            operation.UpdateModel(previous.Document, revision.Document, model);
            VerifyCommit(previous, previousSnapshot, committed, step, violations);
            VerifyModel(revision.Document, model, step, violations);
            if (violations.Count != 0)
            {
                break;
            }
        }

        await Assert.That(violations).IsEmpty();
    }

    private static ModelOperation CreateOperation(
        ProjectRevision revision,
        EditorModel model,
        byte command,
        int step)
    {
        var definitionId = revision.Document.EntryCircuitDefinitionId;
        var point = Point(command);
        return (command % 12) switch
        {
            0 => PlaceComponent(definitionId, point, step),
            1 when model.Components.Count != 0 => MoveComponent(
                definitionId, Select(model.ComponentOrder, command), point),
            2 when model.Components.Count != 0 => RenameComponent(
                definitionId, Select(model.ComponentOrder, command), step),
            3 when model.Components.Count != 0 => RemoveComponent(
                definitionId, Select(model.ComponentOrder, command)),
            4 => CreateAnnotation(definitionId, point, step),
            5 when model.Annotations.Count != 0 => ChangeAnnotation(
                definitionId, Select(model.AnnotationOrder, command), point, step),
            6 when model.Annotations.Count != 0 => MoveAnnotation(
                definitionId, Select(model.AnnotationOrder, command), point),
            7 when model.Annotations.Count != 0 => RemoveAnnotation(
                definitionId, Select(model.AnnotationOrder, command)),
            8 => CreateMemory(command, step),
            9 when model.Memories.Count != 0 => ReplaceMemory(
                Select(model.MemoryOrder, command), command, step),
            10 when model.Memories.Count != 0 => RemoveMemory(
                Select(model.MemoryOrder, command)),
            1 or 2 or 3 => PlaceComponent(definitionId, point, step),
            5 or 6 or 7 => CreateAnnotation(definitionId, point, step),
            9 or 10 => CreateMemory(command, step),
            _ => throw new InvalidOperationException("The model command is undefined."),
        };
    }

    private static ModelOperation PlaceComponent(
        CircuitDefinitionId definitionId,
        GridPoint point,
        int step)
    {
        var state = new ComponentState($"Component {step}", new ComponentPlacement(point));
        return new ModelOperation(
            "place component",
            new PlaceComponentInstanceIntent(
                definitionId,
                Contract("logic.not"),
                WidthParameters(1),
                state.Placement,
                state.DisplayName),
            (before, after, model) =>
            {
                var id = AddedId(
                    before.EntryCircuitDefinition.ComponentInstances.Select(item => item.Id),
                    after.EntryCircuitDefinition.ComponentInstances.Select(item => item.Id));
                model.Components.Add(id, state);
                model.ComponentOrder.Add(id);
            });
    }

    private static ModelOperation MoveComponent(
        CircuitDefinitionId definitionId,
        ComponentInstanceId id,
        GridPoint point)
    {
        var placement = new ComponentPlacement(point, QuarterTurn.One, Reflected: true);
        return new ModelOperation(
            "move component",
            new MoveComponentInstancesIntent(
                definitionId,
                [new ComponentMove(id, placement)]),
            (_, _, model) => model.Components[id] = model.Components[id] with
            {
                Placement = placement,
            });
    }

    private static ModelOperation RenameComponent(
        CircuitDefinitionId definitionId,
        ComponentInstanceId id,
        int step)
    {
        var displayName = $"Renamed component {step}";
        return new ModelOperation(
            "rename component",
            new RenameComponentInstanceIntent(definitionId, id, displayName),
            (_, _, model) => model.Components[id] = model.Components[id] with
            {
                DisplayName = displayName,
            });
    }

    private static ModelOperation RemoveComponent(
        CircuitDefinitionId definitionId,
        ComponentInstanceId id) =>
        new(
            "remove component",
            new RemoveComponentInstancesIntent(definitionId, [id]),
            (_, _, model) =>
            {
                model.Components.Remove(id);
                model.ComponentOrder.Remove(id);
            });

    private static ModelOperation CreateAnnotation(
        CircuitDefinitionId definitionId,
        GridPoint point,
        int step)
    {
        var state = new AnnotationState(
            $"Annotation {step}", point, AnnotationAlignment.Center);
        return new ModelOperation(
            "create annotation",
            new CreateAnnotationIntent(definitionId, state.Value),
            (before, after, model) =>
            {
                var id = AddedId(
                    before.EntryCircuitDefinition.Annotations.Select(item => item.Id),
                    after.EntryCircuitDefinition.Annotations.Select(item => item.Id));
                model.Annotations.Add(id, state);
                model.AnnotationOrder.Add(id);
            });
    }

    private static ModelOperation ChangeAnnotation(
        CircuitDefinitionId definitionId,
        AnnotationId id,
        GridPoint point,
        int step)
    {
        var state = new AnnotationState(
            $"Changed annotation {step}", point, AnnotationAlignment.End);
        return new ModelOperation(
            "change annotation",
            new ChangeAnnotationIntent(definitionId, id, state.Value),
            (_, _, model) => model.Annotations[id] = state);
    }

    private static ModelOperation MoveAnnotation(
        CircuitDefinitionId definitionId,
        AnnotationId id,
        GridPoint point) =>
        new(
            "move annotation",
            new MoveAnnotationsIntent(definitionId, [new AnnotationMove(id, point)]),
            (_, _, model) => model.Annotations[id] = model.Annotations[id] with
            {
                Position = point,
            });

    private static ModelOperation RemoveAnnotation(
        CircuitDefinitionId definitionId,
        AnnotationId id) =>
        new(
            "remove annotation",
            new RemoveAnnotationIntent(definitionId, id),
            (_, _, model) =>
            {
                model.Annotations.Remove(id);
                model.AnnotationOrder.Remove(id);
            });

    private static ModelOperation CreateMemory(byte command, int step)
    {
        var state = MemoryState.Create($"Memory {step}", command);
        return new ModelOperation(
            "create memory",
            new CreateMemoryImageIntent(
                state.DisplayName, 2, 2, state.Words),
            (before, after, model) =>
            {
                var id = AddedId(
                    before.MemoryImages.Select(item => item.Id),
                    after.MemoryImages.Select(item => item.Id));
                model.Memories.Add(id, state);
                model.MemoryOrder.Add(id);
            });
    }

    private static ModelOperation ReplaceMemory(
        MemoryImageId id,
        byte command,
        int step)
    {
        var state = MemoryState.Create($"Replaced memory {step}", command);
        return new ModelOperation(
            "replace memory",
            new ReplaceMemoryImageIntent(
                id, state.DisplayName, 2, 2, state.Words, []),
            (_, _, model) => model.Memories[id] = state);
    }

    private static ModelOperation RemoveMemory(MemoryImageId id) =>
        new(
            "remove memory",
            new RemoveMemoryImageIntent(id),
            (_, _, model) =>
            {
                model.Memories.Remove(id);
                model.MemoryOrder.Remove(id);
            });

    private static void VerifyExpectedRejection(
        ProjectRevision revision,
        int step,
        List<string> violations)
    {
        var snapshot = Snapshot(revision.Document);
        var outcome = ProjectEditor.Apply(
            revision,
            new RenameComponentInstanceIntent(
                revision.Document.EntryCircuitDefinitionId,
                new ComponentInstanceId("missing"),
                "Missing"));
        if (outcome is not EditRejected rejected)
        {
            violations.Add($"step {step}: missing component rename was not rejected");
            return;
        }

        if (snapshot != Snapshot(revision.Document))
        {
            violations.Add($"step {step}: rejected edit changed the current document");
        }

        var canonical = AuthoringCanonicalizer.Diagnostics(rejected.Diagnostics);
        if (!canonical.SequenceEqual(rejected.Diagnostics))
        {
            violations.Add($"step {step}: rejection diagnostics were not canonical");
        }
    }

    private static void VerifyCommit(
        ProjectRevision previous,
        string previousSnapshot,
        EditCommitted committed,
        int step,
        List<string> violations)
    {
        if (previous.RevisionId == committed.Revision.RevisionId)
        {
            violations.Add($"step {step}: commit reused the Project Revision ID");
        }

        if (previousSnapshot != Snapshot(previous.Document))
        {
            violations.Add($"step {step}: commit mutated the previous revision");
        }

        ProjectEditor.ValidateDocument(committed.Revision.Document);
        _ = new ProjectImportCandidate(committed.Revision.Document);
        VerifySources("changed", committed.ChangedSources, step, violations);
        VerifySources("removed", committed.RemovedSources, step, violations);
    }

    private static void VerifySources(
        string kind,
        ReadOnlyCollection<AuthoredSourceIdentity> sources,
        int step,
        List<string> violations)
    {
        if (sources.Distinct().Count() != sources.Count
            || !AuthoringCanonicalizer.Sources(sources).SequenceEqual(sources))
        {
            violations.Add($"step {step}: {kind} sources were not unique and canonical");
        }
    }

    private static void VerifyModel(
        ProjectDocument document,
        EditorModel model,
        int step,
        List<string> violations)
    {
        var definition = document.EntryCircuitDefinition;
        if (definition.ComponentInstances.Count != model.Components.Count
            || definition.Annotations.Count != model.Annotations.Count
            || document.MemoryImages.Count != model.Memories.Count)
        {
            violations.Add($"step {step}: collection counts diverged from the model");
            return;
        }

        foreach (var (id, expected) in model.Components)
        {
            var actual = definition.ComponentInstances.SingleOrDefault(item => item.Id == id);
            if (actual is null
                || actual.DisplayName != expected.DisplayName
                || actual.Placement != expected.Placement
                || actual.Target is not LibraryComponentTarget target
                || target.ContractKey != Contract("logic.not"))
            {
                violations.Add($"step {step}: component {id.Value} diverged from the model");
                return;
            }
        }

        if (!definition.Annotations.Select(item => item.Id)
                .SequenceEqual(model.AnnotationOrder))
        {
            violations.Add($"step {step}: annotation z-order diverged from the model");
            return;
        }

        foreach (var (id, expected) in model.Annotations)
        {
            var actual = definition.Annotations.Single(item => item.Id == id);
            if (actual.Text != expected.Text
                || actual.Position != expected.Position
                || actual.Alignment != expected.Alignment)
            {
                violations.Add($"step {step}: annotation {id.Value} diverged from the model");
                return;
            }
        }

        foreach (var (id, expected) in model.Memories)
        {
            var actual = document.MemoryImages.Single(item => item.Id == id);
            if (actual.DisplayName != expected.DisplayName
                || !expected.Cells.SequenceEqual(MemoryCells(actual)))
            {
                violations.Add($"step {step}: memory {id.Value} diverged from the model");
                return;
            }
        }
    }

    private static TId AddedId<TId>(IEnumerable<TId> before, IEnumerable<TId> after)
        where TId : notnull => after.Except(before).Single();

    private static T Select<T>(IReadOnlyList<T> values, byte command) =>
        values[command % values.Count];

    private static GridPoint Point(byte command) =>
        new((command & 0x0f) - 8, ((command >> 4) & 0x0f) - 8);

    private static string Snapshot(ProjectDocument document)
    {
        var builder = new StringBuilder();
        foreach (var image in document.MemoryImages)
        {
            builder.Append("M|").Append(image.Id.Value).Append('|')
                .Append(image.DisplayName).Append('|').Append(image.Width).Append('|')
                .Append(image.Depth).Append('|')
                .AppendJoin(',', MemoryCells(image)).AppendLine();
        }

        foreach (var definition in document.CircuitDefinitions)
        {
            builder.Append("D|").Append(definition.Id.Value).Append('|')
                .Append(definition.DisplayName).AppendLine();
            foreach (var instance in definition.ComponentInstances)
            {
                builder.Append("C|").Append(instance.Id.Value).Append('|')
                    .Append(instance.DisplayName).Append('|')
                    .Append(instance.Placement).AppendLine();
            }

            foreach (var annotation in definition.Annotations)
            {
                builder.Append("A|").Append(annotation.Id.Value).Append('|')
                    .Append(annotation.Text).Append('|').Append(annotation.Position)
                    .Append('|').Append(annotation.Alignment).AppendLine();
            }
        }

        return builder.ToString();
    }

    private static LogicValue[] MemoryCells(MemoryImage image) =>
    [
        image[0, 0], image[0, 1], image[1, 0], image[1, 1],
    ];

    private sealed record ModelOperation(
        string Name,
        EditIntent Intent,
        Action<ProjectDocument, ProjectDocument, EditorModel> UpdateModel);

    private sealed class EditorModel
    {
        public Dictionary<ComponentInstanceId, ComponentState> Components { get; } = [];

        public List<ComponentInstanceId> ComponentOrder { get; } = [];

        public Dictionary<AnnotationId, AnnotationState> Annotations { get; } = [];

        public List<AnnotationId> AnnotationOrder { get; } = [];

        public Dictionary<MemoryImageId, MemoryState> Memories { get; } = [];

        public List<MemoryImageId> MemoryOrder { get; } = [];
    }

    private sealed record ComponentState(
        string DisplayName,
        ComponentPlacement Placement);

    private sealed record AnnotationState(
        string Text,
        GridPoint Position,
        AnnotationAlignment Alignment)
    {
        public AnnotationValue Value => new(Text, Position, Alignment);
    }

    private sealed record MemoryState(
        string DisplayName,
        MemoryImageWord[] Words,
        LogicValue[] Cells)
    {
        public static MemoryState Create(string displayName, byte command)
        {
            LogicValue[] cells =
            [
                Value(command),
                Value(command >> 2),
                Value(command >> 4),
                Value(command >> 6),
            ];
            return new MemoryState(
                displayName,
                [new MemoryImageWord(cells[..2]), new MemoryImageWord(cells[2..])],
                cells);
        }

        private static LogicValue Value(int value) => (value % 3) switch
        {
            0 => LogicValue.Zero,
            1 => LogicValue.One,
            _ => LogicValue.X,
        };
    }
}
