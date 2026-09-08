using LogicLab.Application.Examples;
using LogicLab.Application.Workspaces;
using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Engine;
using LogicLab.Engine.Compilation;
using LogicLab.Engine.Simulation;
using LogicLab.ProjectFormat;
using ScheduleStimulus = LogicLab.Engine.Simulation.ScheduleStimulusBatch;

namespace LogicLab.Application.Tests;

internal sealed class ExampleArithmeticTests
{
    [Test]
    public async Task CarryLookahead_AllFourBitOperandsAndCarryInputs_MatchesIntegerAddition()
    {
        using var circuit = await ExampleCircuit.OpenAsync(ExampleProject.CarryLookahead);
        ulong time = 0;
        for (var a = 0; a < 16; a++)
        {
            for (var b = 0; b < 16; b++)
            {
                for (var carry = 0; carry < 2; carry++)
                {
                    circuit.Schedule(++time, ("A", a), ("B", b), ("Carry in", carry));
                    circuit.AdvanceTo(time);
                    await Assert.That(circuit.Output("Sum") + (16 * circuit.Output("Carry out")))
                        .IsEqualTo(a + b + carry);
                }
            }
        }
    }

    [Test]
    public async Task BitSerial_AllFourBitOperands_LoadsAndAddsInFourRisingEdges()
    {
        using var circuit = await ExampleCircuit.OpenAsync(ExampleProject.BitSerial);
        circuit.AdvanceTo(7);
        await Assert.That(circuit.Output("Serial result") + (16 * circuit.Output("Carry out")))
            .IsEqualTo(17);
        for (var a = 0; a < 16; a++)
        {
            for (var b = 0; b < 16; b++)
            {
                var loadTime = circuit.Time + 1;
                if (loadTime % 2 == 0)
                {
                    loadTime++;
                }

                circuit.Schedule(loadTime, ("A", a), ("B", b), ("Load operands", 1));
                circuit.AdvanceTo(loadTime);
                await Assert.That(circuit.Output("Serial result")).IsEqualTo(0);
                await Assert.That(circuit.Output("Carry out")).IsEqualTo(0);
                circuit.Schedule(loadTime + 1, ("Load operands", 0));
                circuit.AdvanceTo(loadTime + 8);
                await Assert.That(circuit.Output("Serial result") + (16 * circuit.Output("Carry out")))
                    .IsEqualTo(a + b);
            }
        }
    }

    private sealed class ExampleCircuit(
        ProjectRevision revision,
        CompilationArtifact artifact,
        SimulationOpened opened) : IDisposable
    {
        public ulong Time => Snapshot().LogicalTime;

        public static async Task<ExampleCircuit> OpenAsync(ExampleProject example)
        {
            await using var source = ExampleProjects.Open(example);
            var read = (PackageReadSucceeded)await ProjectPackage.ReadAsync(
                new ProjectPackageReadRequest(source, PackagePolicy.Default), CancellationToken.None);
            var revision = ((ProjectGenesisCommitted)ProjectEditor.Begin(
                new ImportedProjectSeed(read.ImportCandidate))).Revision;
            var compilation = (CompilationSucceeded)Compiler.Compile(new CompilationRequest(
                revision, revision.Document.EntryCircuitDefinitionId, revision.Document.LibrarySnapshot,
                new ProjectScalePolicy("example-tests", "1",
                    [.. Enum.GetValues<ProjectScaleDimension>().Select(dimension => new ProjectScaleLimit(dimension, 1_000_000))])),
                CancellationToken.None);
            var configuration = SessionConfigurationV1.ForEntryOutputs(revision);
            var opened = (SimulationOpened)SimulationRuntime.Open(new OpenSimulationRequest(
                compilation.Artifact,
                new SimulationSessionConfiguration(configuration.SimulationPolicy, configuration.TracePolicy, configuration.InitialProbes),
                WorkspaceSessionPolicies.Simulation, WorkspaceSessionPolicies.Trace), CancellationToken.None);
            return new ExampleCircuit(revision, compilation.Artifact, opened);
        }

        public void Schedule(ulong time, params (string Name, int Value)[] values)
        {
            var assignments = values.Select(value =>
            {
                var instance = Instance(value.Name, "source.input");
                var width = ((Unsigned32ParameterValue)instance.Parameters.Single(parameter => parameter.ParameterId == "width").Value).Value;
                var source = artifact.SourceMap.Drivers.Single(driver => driver.Source.Identity is InstancePortSourceIdentity identity
                    && identity.ComponentInstanceId == instance.Id).Source;
                return new StimulusAssignment(source, new LogicVector(
                    [.. Enumerable.Range(0, checked((int)width)).Select(bit => (value.Value & (1 << bit)) == 0 ? LogicValue.Zero : LogicValue.One)]));
            }).ToArray();
            if (SimulationRuntime.Execute(opened.Handle, new ScheduleStimulus(new StimulusBatch(time, assignments)), CancellationToken.None)
                is not StimulusBatchScheduled)
            {
                throw new InvalidOperationException("The example rejected a valid input batch.");
            }
        }

        public void AdvanceTo(ulong time)
        {
            while (Time < time)
            {
                if (SimulationRuntime.Execute(opened.Handle, new AdvanceToNextQuiescentBoundary(), CancellationToken.None)
                    is not AdvanceCommitted)
                {
                    throw new InvalidOperationException("The example failed to reach the requested boundary.");
                }
            }

            if (Time != time)
            {
                throw new InvalidOperationException("The example skipped the requested boundary.");
            }
        }

        public int Output(string name)
        {
            var instance = Instance(name, "sink.output");
            var net = revision.Document.EntryCircuitDefinition.Nets.Single(candidate => candidate.Terminals
                .OfType<InstanceTerminalReference>().Any(terminal => terminal.ComponentInstanceId == instance.Id));
            var value = Snapshot().Probes.Single(probe => probe.Source.Identity is NetSourceIdentity identity && identity.NetId == net.Id).Value;
            var result = 0;
            for (var bit = 0; bit < value.Width; bit++)
            {
                result |= value[bit] switch
                {
                    LogicValue.Zero => 0,
                    LogicValue.One => 1 << bit,
                    _ => throw new InvalidOperationException("The example produced an unknown bit for binary inputs."),
                };
            }

            return result;
        }

        public void Dispose() => SimulationRuntime.Close(opened.Handle);

        private ComponentInstance Instance(string name, string contractId) => revision.Document.EntryCircuitDefinition.ComponentInstances
            .Single(instance => instance.DisplayName == name
                && instance.Target is LibraryComponentTarget library && library.ContractKey.ContractId == contractId);

        private SessionSnapshotRead Snapshot() => (SessionSnapshotRead)SimulationRuntime.Read(
            opened.Handle, new ReadSessionSnapshot(), CancellationToken.None);
    }
}
