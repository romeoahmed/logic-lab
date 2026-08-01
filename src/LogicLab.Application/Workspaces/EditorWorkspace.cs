using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.Engine;
using LogicLab.Engine.Compilation;
using LogicLab.Engine.Simulation;

namespace LogicLab.Application.Workspaces;

public sealed class EditorWorkspace
{
    private readonly Lock gate = new();
    private readonly Dictionary<WorkspaceId, WorkspaceState> workspaces = [];

    public Task<WorkspaceOpenOutcome> OpenAsync(
        OpenWorkspaceRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult<WorkspaceOpenOutcome>(
                new WorkspaceOpenRejected("operation_cancelled", []));
        }

        if (request is not CreateSandbox create)
        {
            return Task.FromResult<WorkspaceOpenOutcome>(
                new WorkspaceOpenRejected("workspace_request_unsupported", []));
        }

        var genesis = ProjectEditor.Begin(new NewProjectSeed(
            create.ProjectDisplayName,
            LibrarySnapshot.Core,
            new SymbolProfileReference(
                "TeachingMixed",
                "1.0.0",
                IndicationConvention.Negation),
            create.EntryCircuitDefinitionDisplayName));
        if (genesis is ProjectGenesisRejected rejected)
        {
            return Task.FromResult<WorkspaceOpenOutcome>(new WorkspaceOpenRejected(
                    rejected.Reason,
                    rejected.Diagnostics.Select(item => item.Code).ToArray()));
        }

        var committed = (ProjectGenesisCommitted)genesis;
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult<WorkspaceOpenOutcome>(
                new WorkspaceOpenRejected("operation_cancelled", []));
        }

        var id = WorkspaceId.Create();
        var state = new WorkspaceState(id, committed.Revision);
        lock (gate)
        {
            workspaces.Add(id, state);
        }

        return Task.FromResult<WorkspaceOpenOutcome>(
            new WorkspaceOpened(id, Project(state)));
    }

    public async Task<WorkspaceCommandOutcome> DispatchAsync(
        WorkspaceCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (cancellationToken.IsCancellationRequested)
        {
            return Reject("operation_cancelled");
        }

        WorkspaceState state;
        lock (gate)
        {
            if (!workspaces.TryGetValue(command.WorkspaceId, out state!))
            {
                return Reject("workspace_not_found");
            }
        }

        try
        {
            await state.CommandGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Reject("operation_cancelled");
        }

        try
        {
            return await Task.Run(() => command switch
            {
                ApplyEdit apply => Apply(state, apply, cancellationToken),
                RequestCompilation => Compile(state, cancellationToken),
                CreateSession => OpenSession(state, cancellationToken),
                ScheduleInputStimulus schedule => Schedule(
                    state,
                    schedule,
                    cancellationToken),
                StepSession => Step(state, cancellationToken),
                _ => Reject("workspace_command_unsupported"),
            }).ConfigureAwait(false);
        }
        finally
        {
            state.CommandGate.Release();
        }
    }

    public async Task<WorkspaceReadOutcome> ReadAsync(
        WorkspaceId workspaceId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workspaceId);
        if (cancellationToken.IsCancellationRequested)
        {
            return new WorkspaceReadRejected("operation_cancelled");
        }

        WorkspaceState state;
        lock (gate)
        {
            if (!workspaces.TryGetValue(workspaceId, out state!))
            {
                return new WorkspaceReadRejected("workspace_not_found");
            }
        }

        try
        {
            await state.CommandGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return new WorkspaceReadRejected("operation_cancelled");
        }

        try
        {
            return new ProjectionSnapshot(Project(state));
        }
        finally
        {
            state.CommandGate.Release();
        }
    }

    private static WorkspaceCommandOutcome Apply(
        WorkspaceState state,
        ApplyEdit command,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Reject("operation_cancelled");
        }

        if (state.SessionHandle is not null)
        {
            return Reject("session_active");
        }

        var outcome = ProjectEditor.Apply(state.Revision, command.Intent);
        if (outcome is EditRejected rejected)
        {
            return Reject(
                "authoring_rejected",
                rejected.Diagnostics.Select(item => item.Code));
        }

        var committed = (EditCommitted)outcome;
        state.Revision = committed.Revision;
        state.Artifact = null;
        state.Compilation = NotRequestedCompilation();
        state.ProjectionVersion++;
        return new AuthoringCommitted(
            state.Revision.RevisionId,
            state.ProjectionVersion);
    }

    private static WorkspaceCommandOutcome Compile(
        WorkspaceState state,
        CancellationToken cancellationToken)
    {
        if (state.SessionHandle is not null)
        {
            return Reject("session_active");
        }

        var outcome = Compiler.Compile(
            new CompilationRequest(
                state.Revision,
                state.Revision.Document.EntryCircuitDefinitionId,
                state.Revision.Document.LibrarySnapshot,
                DevelopmentProjectScalePolicy),
            cancellationToken);
        if (outcome is CompilationRejected rejected)
        {
            if (string.Equals(
                    rejected.Reason,
                    "compilation_cancelled",
                    StringComparison.Ordinal))
            {
                return Reject("operation_cancelled");
            }

            state.ProjectionVersion++;
            state.Artifact = null;
            state.Compilation = new CompilationProjection(
                CompilationPublicationStatus.Rejected,
                null,
                rejected.Diagnostics.Select(item => item.Code).ToArray());
            return Reject(
                "compilation_rejected",
                state.Compilation.DiagnosticCodes);
        }

        var succeeded = (CompilationSucceeded)outcome;
        state.ProjectionVersion++;
        state.Artifact = succeeded.Artifact;
        state.Compilation = new CompilationProjection(
            CompilationPublicationStatus.Published,
            succeeded.Artifact.Key,
            succeeded.Diagnostics.Select(item => item.Code).ToArray());
        return new CompilationPublished(
            succeeded.Artifact.Key,
            state.ProjectionVersion);
    }

    private static WorkspaceCommandOutcome OpenSession(
        WorkspaceState state,
        CancellationToken cancellationToken)
    {
        if (state.SessionHandle is not null)
        {
            return Reject("session_already_created");
        }

        if (state.Artifact is null)
        {
            return Reject("compilation_required");
        }

        var probeSources = OutputProbeSources(state.Revision, state.Artifact);
        if (probeSources.Length == 0)
        {
            return Reject("output_probe_required");
        }

        var outcome = SimulationRuntime.Open(
            new OpenSimulationRequest(
                state.Artifact,
                new SimulationSessionConfiguration(
                    new SimulationPolicyReference("workbench-simulation", "1"),
                    new TracePolicyReference("workbench-trace", "1"),
                    probeSources),
                DevelopmentSimulationPolicy,
                DevelopmentTracePolicy),
            cancellationToken);
        if (outcome is SimulationOpenRejected
            {
                Reason: SimulationFailureReason.SimulationCancelled,
            })
        {
            return Reject("operation_cancelled");
        }

        if (outcome is not SimulationOpened opened)
        {
            return Reject("simulation_open_rejected");
        }

        var simulation = ReadSimulation(opened.Handle);
        state.SessionHandle = opened.Handle;
        state.Simulation = simulation;
        state.ProjectionVersion++;
        return new SimulationSessionCreated(
            opened.SessionId,
            state.ProjectionVersion);
    }

    private static WorkspaceCommandOutcome Schedule(
        WorkspaceState state,
        ScheduleInputStimulus command,
        CancellationToken cancellationToken)
    {
        if (state.SessionHandle is null || state.Artifact is null)
        {
            return Reject("session_required");
        }

        if (command.Assignments.Count == 0)
        {
            return Reject("stimulus_assignment_required");
        }

        var assignments = new List<StimulusAssignment>(command.Assignments.Count);
        var definition = state.Revision.Document.EntryCircuitDefinition;
        foreach (var assignment in command.Assignments)
        {
            if (assignment is null
                || assignment.Value.Count == 0
                || assignment.Value.Any(value => !Enum.IsDefined(value)))
            {
                return Reject("session_precondition_failed");
            }

            var input = definition.ComponentInstances.SingleOrDefault(instance =>
                instance.Id == assignment.InputComponentInstanceId);
            var width = input?.Parameters
                .SingleOrDefault(parameter => string.Equals(
                    parameter.ParameterId,
                    "width",
                    StringComparison.Ordinal))
                ?.Value as Unsigned32ParameterValue;
            if (input is null
                || !string.Equals(
                    input.ContractKey.LibraryId,
                    CoreLibrarySchema.LibraryId,
                    StringComparison.Ordinal)
                || !string.Equals(
                    input.ContractKey.ContractId,
                    "source.input",
                    StringComparison.Ordinal)
                || width is null
                || assignment.Value.Count != checked((int)width.Value))
            {
                return Reject("session_precondition_failed");
            }

            var source = state.Artifact.SourceMap.Drivers
                .Select(item => item.Source)
                .SingleOrDefault(item => item.Identity is InstancePortSourceIdentity port
                    && port.ComponentInstanceId == assignment.InputComponentInstanceId
                    && string.Equals(port.PortId, "Q", StringComparison.Ordinal));
            if (source is null)
            {
                return Reject("session_precondition_failed");
            }

            assignments.Add(new StimulusAssignment(
                source,
                new LogicVector(assignment.Value)));
        }

        var outcome = SimulationRuntime.Execute(
            state.SessionHandle,
            new LogicLab.Engine.Simulation.ScheduleStimulusBatch(
                new StimulusBatch(command.LogicalTime, assignments)),
            cancellationToken);
        if (outcome is SimulationCommandFailed
            {
                Reason: SimulationFailureReason.SimulationCancelled,
            })
        {
            return Reject("operation_cancelled");
        }

        if (outcome is not StimulusBatchScheduled scheduled)
        {
            return Reject("stimulus_rejected");
        }

        state.Simulation = ReadSimulation(state.SessionHandle);
        state.ProjectionVersion++;
        return new StimulusScheduled(
            scheduled.ScheduledLogicalTime,
            state.ProjectionVersion);
    }

    private static WorkspaceCommandOutcome Step(
        WorkspaceState state,
        CancellationToken cancellationToken)
    {
        if (state.SessionHandle is null)
        {
            return Reject("session_required");
        }

        var outcome = SimulationRuntime.Execute(
            state.SessionHandle,
            new AdvanceToNextQuiescentBoundary(),
            cancellationToken);
        if (outcome is AdvanceFailed
            {
                Reason: SimulationFailureReason.SimulationCancelled,
            })
        {
            return Reject("operation_cancelled");
        }

        if (outcome is not AdvanceCommitted committed)
        {
            return outcome is NoScheduledStimulus
                ? Reject("scheduled_stimulus_required")
                : Reject("simulation_step_rejected");
        }

        state.Simulation = ReadSimulation(state.SessionHandle);
        state.ProjectionVersion++;
        return new SessionStepped(committed.LogicalTime, state.ProjectionVersion);
    }

    private static CompilationSource[] OutputProbeSources(
        ProjectRevision revision,
        CompilationArtifact artifact)
    {
        var definition = revision.Document.EntryCircuitDefinition;
        var outputIds = definition.ComponentInstances
            .Where(instance => instance.ContractKey.ContractId == "sink.output")
            .Select(instance => instance.Id)
            .ToHashSet();
        var netIds = definition.Nets
            .Where(net => net.Terminals.Any(terminal =>
                outputIds.Contains(terminal.ComponentInstanceId)
                && string.Equals(terminal.PortId, "D", StringComparison.Ordinal)))
            .Select(net => net.Id)
            .ToHashSet();

        return artifact.SourceMap.Nets
            .Select(item => item.Source)
            .Where(source => source.Identity is NetSourceIdentity net
                && netIds.Contains(net.NetId))
            .OrderBy(source => ((NetSourceIdentity)source.Identity).NetId.Value)
            .ToArray();
    }

    private static SimulationProjection ReadSimulation(SimulationSessionHandle handle)
    {
        var outcome = SimulationRuntime.Read(
            handle,
            new ReadSessionSnapshot(),
            CancellationToken.None);
        if (outcome is not SessionSnapshotRead snapshot)
        {
            throw new InvalidOperationException("The simulation snapshot could not be read.");
        }

        return new SimulationProjection(
            snapshot.SessionId,
            snapshot.SessionVersion,
            snapshot.LogicalTime,
            snapshot.TraceCursor,
            snapshot.Probes.Select(probe => new ProbeProjection(
                probe.ProbeId,
                probe.Source.Identity,
                Values(probe.Value))).ToArray());
    }

    private static LogicValue[] Values(LogicVector vector)
    {
        return Enumerable.Range(0, vector.Width).Select(index => vector[index]).ToArray();
    }

    private static WorkspaceProjection Project(WorkspaceState state)
    {
        return new WorkspaceProjection(
            state.Id,
            state.ProjectionVersion,
            state.Revision,
            state.Compilation,
            state.Simulation);
    }

    private static WorkspaceCommandRejected Reject(
        string code,
        IEnumerable<string>? diagnosticCodes = null)
    {
        return new WorkspaceCommandRejected(code, diagnosticCodes?.ToArray() ?? []);
    }

    private static CompilationProjection NotRequestedCompilation()
    {
        return new CompilationProjection(CompilationPublicationStatus.NotRequested, null, []);
    }

    private static ProjectScalePolicy DevelopmentProjectScalePolicy { get; } = new(
        "workbench-project-scale",
        "1",
        [
            new ProjectScaleLimit(ProjectScaleDimension.DefinitionCount, 100),
            new ProjectScaleLimit(ProjectScaleDimension.EntityCount, 10_000),
            new ProjectScaleLimit(ProjectScaleDimension.HierarchyDepth, 100),
            new ProjectScaleLimit(ProjectScaleDimension.ElaboratedSlotCount, 100_000),
            new ProjectScaleLimit(ProjectScaleDimension.MemoryCellCount, 100_000),
        ]);

    private static SimulationPolicy DevelopmentSimulationPolicy { get; } = new(
        "workbench-simulation",
        "1",
        [
            new SimulationLimit(SimulationDimension.ScheduledBatchCount, 10_000),
            new SimulationLimit(SimulationDimension.ScheduledAssignmentCount, 100_000),
            new SimulationLimit(SimulationDimension.AdvanceWorkItemCount, 1_000_000),
            new SimulationLimit(SimulationDimension.AdvanceFrontierItemCount, 1_000_000),
            new SimulationLimit(SimulationDimension.WorkingLayerSlotCount, 1_000_000),
            new SimulationLimit(SimulationDimension.TriggerBatchCount, 100_000),
            new SimulationLimit(SimulationDimension.ZeroTimeStateCount, 100_000),
        ]);

    private static TracePolicy DevelopmentTracePolicy { get; } = new(
        "workbench-trace",
        "1",
        [
            new TraceLimit(TraceDimension.ProbeCount, 1_000),
            new TraceLimit(TraceDimension.RetainedTransitionCount, 1_000_000),
            new TraceLimit(TraceDimension.SealedChunkCount, 100_000),
            new TraceLimit(TraceDimension.RetainedBytes, 100_000_000),
            new TraceLimit(TraceDimension.DeltaDebugRecordCount, 1),
        ]);

    private sealed class WorkspaceState(WorkspaceId id, ProjectRevision revision)
    {
        public WorkspaceId Id { get; } = id;

        public ulong ProjectionVersion { get; set; } = 1;

        public ProjectRevision Revision { get; set; } = revision;

        public CompilationArtifact? Artifact { get; set; }

        public CompilationProjection Compilation { get; set; } = NotRequestedCompilation();

        public SimulationSessionHandle? SessionHandle { get; set; }

        public SimulationProjection? Simulation { get; set; }

        public SemaphoreSlim CommandGate { get; } = new(1, 1);
    }
}
