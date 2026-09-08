using System.Collections.ObjectModel;
using LogicLab.Domain.Authoring;
using LogicLab.Engine.Compilation;
using LogicLab.Engine.Simulation;

namespace LogicLab.Application.Workspaces;

public abstract record WorkspaceCommand
{
    private protected WorkspaceCommand(WorkspaceCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Context = context;
    }

    public WorkspaceId WorkspaceId => Context.WorkspaceId;

    public WorkspaceCommandContext Context { get; }
}

public sealed record ApplyEdit : WorkspaceCommand
{
    public ApplyEdit(
        WorkspaceCommandContext context,
        AuthoringPrecondition precondition,
        EditIntent intent)
        : base(context)
    {
        ArgumentNullException.ThrowIfNull(precondition);
        ArgumentNullException.ThrowIfNull(intent);
        Precondition = precondition;
        Intent = intent;
    }

    public EditIntent Intent { get; }

    public AuthoringPrecondition Precondition { get; }
}

public sealed record RequestCompilation : WorkspaceCommand
{
    public RequestCompilation(
        WorkspaceCommandContext context,
        CompilationPrecondition precondition)
        : base(context)
    {
        ArgumentNullException.ThrowIfNull(precondition);
        Precondition = precondition;
    }

    public CompilationPrecondition Precondition { get; }
}

public sealed record CreateSession : WorkspaceCommand
{
    public CreateSession(
        WorkspaceCommandContext context,
        SessionCreationPrecondition precondition,
        SessionConfigurationV1 configuration)
        : base(context)
    {
        ArgumentNullException.ThrowIfNull(precondition);
        ArgumentNullException.ThrowIfNull(configuration);
        Precondition = precondition;
        Configuration = configuration;
    }

    public SessionCreationPrecondition Precondition { get; }

    public SessionConfigurationV1 Configuration { get; }
}

public sealed record RestartSession : WorkspaceCommand
{
    public RestartSession(
        WorkspaceCommandContext context,
        SessionMutationPrecondition precondition,
        CompilationArtifactKey targetCompilationArtifactKey,
        SessionConfigurationV1 configuration)
        : base(context)
    {
        ArgumentNullException.ThrowIfNull(precondition);
        ArgumentNullException.ThrowIfNull(targetCompilationArtifactKey);
        ArgumentNullException.ThrowIfNull(configuration);
        Precondition = precondition;
        TargetCompilationArtifactKey = targetCompilationArtifactKey;
        Configuration = configuration;
    }

    public SessionMutationPrecondition Precondition { get; }

    public CompilationArtifactKey TargetCompilationArtifactKey { get; }

    public SessionConfigurationV1 Configuration { get; }
}

public sealed record CloseSession : WorkspaceCommand
{
    public CloseSession(
        WorkspaceCommandContext context,
        SessionMutationPrecondition precondition)
        : base(context)
    {
        ArgumentNullException.ThrowIfNull(precondition);
        Precondition = precondition;
    }

    public SessionMutationPrecondition Precondition { get; }
}

public sealed record ScheduleStimulusBatch : WorkspaceCommand
{
    public ScheduleStimulusBatch(
        WorkspaceCommandContext context,
        SessionMutationPrecondition precondition,
        StimulusBatch batch)
        : base(context)
    {
        ArgumentNullException.ThrowIfNull(precondition);
        ArgumentNullException.ThrowIfNull(batch);
        Precondition = precondition;
        Batch = batch;
    }

    public SessionMutationPrecondition Precondition { get; }

    public StimulusBatch Batch { get; }
}

public sealed record StepSession : WorkspaceCommand
{
    public StepSession(
        WorkspaceCommandContext context,
        SessionMutationPrecondition precondition)
        : base(context)
    {
        ArgumentNullException.ThrowIfNull(precondition);
        Precondition = precondition;
    }

    public SessionMutationPrecondition Precondition { get; }
}

public abstract record ProbeBindingRequest
{
    private protected ProbeBindingRequest(CompilationSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        Source = source;
    }

    public CompilationSource Source { get; }
}

public sealed record RetainProbe : ProbeBindingRequest
{
    public RetainProbe(ProbeId probeId, CompilationSource source)
        : base(source)
    {
        ArgumentNullException.ThrowIfNull(probeId);
        ProbeId = probeId;
    }

    public ProbeId ProbeId { get; }
}

public sealed record CreateProbe : ProbeBindingRequest
{
    public CreateProbe(CompilationSource source)
        : base(source)
    {
    }
}

public sealed record ReplaceProbes : WorkspaceCommand
{
    public ReplaceProbes(
        WorkspaceCommandContext context,
        SessionMutationPrecondition precondition,
        IReadOnlyList<ProbeBindingRequest> bindings)
        : base(context)
    {
        ArgumentNullException.ThrowIfNull(precondition);
        ArgumentNullException.ThrowIfNull(bindings);
        var ownedBindings = bindings.ToArray();
        if (ownedBindings.Any(static binding => binding is null))
        {
            throw new ArgumentException(
                "Probe bindings cannot contain null requests.",
                nameof(bindings));
        }

        Precondition = precondition;
        Bindings = Array.AsReadOnly(ownedBindings);
    }

    public SessionMutationPrecondition Precondition { get; }

    public ReadOnlyCollection<ProbeBindingRequest> Bindings { get; }
}

public sealed record StartRun : WorkspaceCommand
{
    public StartRun(
        WorkspaceCommandContext context,
        SessionMutationPrecondition precondition)
        : base(context)
    {
        ArgumentNullException.ThrowIfNull(precondition);
        Precondition = precondition;
    }

    public SessionMutationPrecondition Precondition { get; }
}

public sealed record PauseRun : WorkspaceCommand
{
    public PauseRun(
        WorkspaceCommandContext context,
        RunControlPrecondition precondition)
        : base(context)
    {
        ArgumentNullException.ThrowIfNull(precondition);
        Precondition = precondition;
    }

    public RunControlPrecondition Precondition { get; }
}

public sealed record HotSwapSession : WorkspaceCommand
{
    public HotSwapSession(
        WorkspaceCommandContext context,
        SessionMutationPrecondition precondition,
        CompilationArtifactKey targetCompilationArtifactKey)
        : base(context)
    {
        ArgumentNullException.ThrowIfNull(precondition);
        ArgumentNullException.ThrowIfNull(targetCompilationArtifactKey);
        Precondition = precondition;
        TargetCompilationArtifactKey = targetCompilationArtifactKey;
    }

    public SessionMutationPrecondition Precondition { get; }

    public CompilationArtifactKey TargetCompilationArtifactKey { get; }
}

public sealed record CloseWorkspace : WorkspaceCommand
{
    public CloseWorkspace(WorkspaceCommandContext context)
        : base(context)
    {
    }
}
