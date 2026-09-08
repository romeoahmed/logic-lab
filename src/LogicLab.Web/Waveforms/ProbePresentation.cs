using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.Engine.Compilation;

namespace LogicLab.Web.Waveforms;

internal static class ProbePresentation
{
    public static string Label(
        ProjectRevision revision,
        CompilationSource compilationSource,
        ProbePresentationLabels labels)
    {
        ArgumentNullException.ThrowIfNull(revision);
        ArgumentNullException.ThrowIfNull(compilationSource);
        if (compilationSource.Identity is not NetSourceIdentity source)
        {
            throw new ArgumentException("A Probe must identify an authored Net.", nameof(compilationSource));
        }

        if (revision.Document.FindCircuitDefinition(source.CircuitDefinitionId) is not { } definition
            || definition.FindNet(source.NetId) is not { } net)
        {
            return "N" + source.NetId.Value[^6..];
        }

        return NetLabel(definition, net, labels);
    }

    public static string NetLabel(CircuitDefinition definition, Net net, ProbePresentationLabels labels)
    {
        string? inputPortLabel = null;
        string? inputLabel = null;
        string? outputLabel = null;
        foreach (var terminal in net.Terminals)
        {
            if (terminal is DefinitionTerminalReference boundary
                && definition.FindPort(boundary.DefinitionPortId) is { } port)
            {
                if (port.Direction == PortDirection.Output)
                {
                    return port.DisplayName;
                }
                if (port.Direction == PortDirection.Input)
                {
                    inputPortLabel ??= port.DisplayName;
                }
            }
            else if (terminal is InstanceTerminalReference instanceTerminal
                && definition.FindComponentInstance(instanceTerminal.ComponentInstanceId) is { } instance
                && instance.Target is LibraryComponentTarget { ContractKey: var key }
                && key.LibraryId == LibrarySnapshot.Core.LibraryId)
            {
                if (key.ContractId == "sink.output")
                {
                    outputLabel ??= instance.DisplayName ?? labels.Output;
                }
                else if (key.ContractId == "source.input")
                {
                    inputLabel ??= instance.DisplayName ?? labels.Input;
                }
            }
        }

        // Prefer output names, then input names, before a generated network label.
        return outputLabel ?? inputPortLabel ?? inputLabel
            ?? FormattableString.Invariant($"N{definition.Nets.IndexOf(net) + 1}");
    }
}

internal readonly record struct ProbePresentationLabels(string Input, string Output);
