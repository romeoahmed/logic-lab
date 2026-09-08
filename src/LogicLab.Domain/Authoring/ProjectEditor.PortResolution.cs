using LogicLab.Domain.Components;

namespace LogicLab.Domain.Authoring;

public static partial class ProjectEditor
{
    private abstract class AuthoringPortResolution
    {
        private protected AuthoringPortResolution()
        {
        }

        public abstract bool HasPortCount(int count);

        public abstract ResolvedComponentPortSchema? FindPort(string id);

        public abstract bool HasSameShape(AuthoringPortResolution other);
    }

    private sealed class EmptyAuthoringPorts : AuthoringPortResolution
    {
        public static readonly EmptyAuthoringPorts Instance = new();

        public override bool HasPortCount(int count) => count == 0;

        public override ResolvedComponentPortSchema? FindPort(string id) => null;

        public override bool HasSameShape(AuthoringPortResolution other) => other.HasPortCount(0);
    }

    private sealed class LibraryAuthoringPorts(ComponentPortResolution resolution) : AuthoringPortResolution
    {
        public override bool HasPortCount(int count) =>
            resolution.TryGetPortCount(out var actual) && actual == checked((ulong)count);

        public override ResolvedComponentPortSchema? FindPort(string id) =>
            id.Length != 0 && resolution.TryResolvePort(id, out var port) ? port : null;

        public override bool HasSameShape(AuthoringPortResolution other) =>
            other is LibraryAuthoringPorts library && resolution.HasSameShape(library.Resolution);

        private ComponentPortResolution Resolution => resolution;
    }

    private sealed class DefinitionAuthoringPorts(CircuitDefinition definition) : AuthoringPortResolution
    {
        private readonly Dictionary<string, DefinitionPort> portsById =
            definition.Ports.ToDictionary(port => port.Id.Value, StringComparer.Ordinal);

        public override bool HasPortCount(int count) => definition.Ports.Count == count;

        public override ResolvedComponentPortSchema? FindPort(string id)
        {
            var port = portsById.GetValueOrDefault(id);
            return port is null ? null : new ResolvedComponentPortSchema(port.Id.Value, port.Direction, port.Width);
        }

        // Parameter edits retain the same target definition in the same document.
        public override bool HasSameShape(AuthoringPortResolution other) =>
            other is DefinitionAuthoringPorts ports && ReferenceEquals(definition, ports.Definition);

        private CircuitDefinition Definition => definition;
    }
}
