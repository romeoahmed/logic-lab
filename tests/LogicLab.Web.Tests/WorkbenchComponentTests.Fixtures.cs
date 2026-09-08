using System.Security.Claims;
using Bunit;
using LogicLab.Application.Workspaces;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.ProjectFormat;
using LogicLab.Web.Components.Editor;
using LogicLab.Web.Components.Pages;
using LogicLab.Web.Scene;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace LogicLab.Web.Tests;

internal sealed partial class WorkbenchComponentTests
{
    private static Task Select(IRenderedComponent<Editor> rendered, IReadOnlyList<SceneSourceRefV1> sources) =>
        rendered.InvokeAsync(() => rendered.FindComponent<CircuitSceneHost>().Instance.OnSelect.InvokeAsync(
            new SceneSelectionV1(sources, "replace")));

    private static BunitContext CreateContext()
    {
        return CreateContext(out _);
    }

    private static BunitContext CreateContext(
        out BunitJSModuleInterop attachmentNavigation)
    {
        var context = WebTestContext.CreateBunitContext(out attachmentNavigation);
        context.Services.AddSingleton(TimeProvider.System);
        context.Services.AddSingleton(PackagePolicy.Default);
        context.Services.AddSingleton<ProjectImportWorkflow>();
        return context;
    }

    private static async Task<byte[]> CreatePackageAsync()
    {
        var revision = ((ProjectGenesisCommitted)ProjectEditor.Begin(
            new NewProjectSeed(
                "Uploaded project",
                LibrarySnapshot.Core,
                new SymbolProfileReference(
                    "TeachingMixed",
                    "1.0.0",
                    IndicationConvention.Negation),
                "Main"))).Revision;
        await using var carrier = new MemoryStream();
        var outcome = await ProjectPackage.WriteAsync(
            new ProjectPackageWriteRequest(
                revision,
                carrier,
                PackagePolicy.Default),
            CancellationToken.None);
        if (outcome is not PackageWriteSucceeded)
        {
            throw new InvalidOperationException("Test package write failed.");
        }

        return carrier.ToArray();
    }

    private static IRenderedComponent<Editor> RenderEditor(
        BunitContext context,
        IEditorWorkspace workspace,
        bool isInteractive = true)
    {
        context.Services.AddSingleton(workspace);
        context.Renderer.SetRendererInfo(new RendererInfo(
            isInteractive ? "Server" : "Static",
            isInteractive));
        return context.Render<Editor>();
    }

    private static IRenderedComponent<Editor> RenderAuthenticatedEditor(
        BunitContext context,
        IEditorWorkspace workspace)
    {
        context.Services.AddSingleton(workspace);
        context.Renderer.SetRendererInfo(new RendererInfo("Server", isInteractive: true));
        var authenticationState = Task.FromResult(new AuthenticationState(
            new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, "component-user"),
            ], "test"))));
        var host = context.Render<CascadingValue<Task<AuthenticationState>>>(parameters =>
            parameters
                .Add(value => value.Value, authenticationState)
                .AddChildContent<Editor>());
        return host.FindComponent<Editor>();
    }

    private static async Task<IRenderedComponent<Editor>> RenderAuthoredEditor(
        BunitContext context,
        IEditorWorkspace workspace)
    {
        var rendered = RenderEditor(context, workspace);
        _ = await rendered.WaitForElementAsync("[data-command='create']:not([disabled])");
        await ClickAndWaitForState(
            rendered,
            "create",
            () => !IsDisabled(rendered, "export"));
        await AuthorInverterAsync(
            rendered,
            () => !IsDisabled(rendered, "compile"));
        return rendered;
    }

    private static async Task ClickAndWaitForState(
        IRenderedComponent<Editor> rendered,
        string command,
        Func<bool> statePredicate)
    {
        await rendered.Find($"[data-command='{command}']").ClickAsync();
        await rendered.WaitForStateAsync(statePredicate);
    }

    private static async Task ApplyHighInputsAndWait(
        IRenderedComponent<Editor> rendered, Func<bool> statePredicate)
    {
        foreach (var input in rendered.FindAll("[data-stimulus-input]"))
        {
            await input.TriggerEventAsync("ontextimmediate", new ChangeEventArgs
            {
                Value = new string('1', int.Parse(input.GetAttribute("maxlength")!, System.Globalization.CultureInfo.InvariantCulture)),
            });
        }
        await ClickAndWaitForState(rendered, "stimulus", statePredicate);
    }

    private static CircuitDefinition? CurrentDefinition(
        IRenderedComponent<Editor> rendered)
    {
        var host = rendered.FindComponents<CircuitSceneHost>().SingleOrDefault()?.Instance;
        return host?.ProjectRevision.Document.FindCircuitDefinition(host.CircuitDefinitionId);
    }

    private static bool IsDisabled<TComponent>(
        IRenderedComponent<TComponent> rendered,
        string command)
        where TComponent : IComponent
    {
        var commands = rendered.FindAll($"[data-command='{command}']");
        return commands.Count == 0 || commands[0].HasAttribute("disabled");
    }

    private static bool AreAllCommandsDisabled(IRenderedComponent<Editor> rendered)
    {
        var commands = rendered.FindAll("[data-command]");
        return commands.Count > 0
            && commands.All(command => command.HasAttribute("disabled"));
    }

    private sealed class FailingImportWorkspace : DelegatingEditorWorkspace
    {
        public IOException Failure { get; } = new("Imported Workspace could not be opened.");

        public override Task<WorkspaceOpenOutcome> OpenAsync(
            OpenWorkspaceRequest request,
            CancellationToken cancellationToken) => Task.FromException<WorkspaceOpenOutcome>(Failure);
    }

    private sealed class FailingSceneWorkspace : TrackingWorkspace
    {
        public bool FailCommands { get; set; }

        public InvalidOperationException Failure { get; } = new("Scene command execution failed.");

        public override Task<WorkspaceCommandOutcome> DispatchAsync(
            WorkspaceCommand command,
            CancellationToken cancellationToken) => FailCommands
                ? Task.FromException<WorkspaceCommandOutcome>(Failure)
                : base.DispatchAsync(command, cancellationToken);
    }

    private sealed class RecordingAttachmentWorkspace : DelegatingEditorWorkspace
    {
        public List<AttachRequest> AttachRequests { get; } = [];

        public List<Attached> Attachments { get; } = [];

        public override async Task<WorkspaceAttachOutcome> AttachAsync(
            AttachRequest request,
            CancellationToken cancellationToken)
        {
            AttachRequests.Add(request);
            var outcome = await base.AttachAsync(request, cancellationToken);
            if (outcome is Attached attached)
            {
                Attachments.Add(attached);
            }

            return outcome;
        }
    }

    private sealed class DurableWorkflowWorkspace(IDurableProjectRepository repository)
        : DelegatingEditorWorkspace(durableProjectRepository: repository)
    {
        public List<OpenWorkspaceRequest> OpenRequests { get; } = [];

        public override Task<WorkspaceOpenOutcome> OpenAsync(
            OpenWorkspaceRequest request,
            CancellationToken cancellationToken)
        {
            OpenRequests.Add(request);
            return base.OpenAsync(request, cancellationToken);
        }
    }

    private sealed class InMemoryDurableProjectRepository : IDurableProjectRepository
    {
        private DurableVersion? currentVersion;

        public bool ConflictOnSave { get; init; }

        public int ClaimCallCount { get; private set; }

        public int SaveCallCount { get; private set; }

        public DurableProjectClaimRequest? LastClaim { get; private set; }

        public Task<DurableProjectClaimRepositoryOutcome> ClaimAsync(
            DurableProjectClaimRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ClaimCallCount++;
            LastClaim = request;
            currentVersion = request.InitialDurableVersion;
            return Task.FromResult<DurableProjectClaimRepositoryOutcome>(
                new DurableProjectClaimStored(
                    request.DurableProjectId,
                    request.InitialDurableVersion,
                    request.ProjectRevision.RevisionId,
                    request.DisplayName));
        }

        public Task<DurableProjectClaimRepositoryOutcome?> TryReadClaimReceiptAsync(
            DurableProjectClaimRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<DurableProjectClaimRepositoryOutcome?>(null);
        }

        public Task<DurableProjectSaveRepositoryOutcome> SaveAsync(
            DurableProjectSaveRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SaveCallCount++;
            if (ConflictOnSave)
            {
                return Task.FromResult<DurableProjectSaveRepositoryOutcome>(
                    new DurableProjectSaveRepositoryConflict(
                        request.ExpectedDurableVersion,
                        request.NextDurableVersion));
            }

            if (request.ExpectedDurableVersion != currentVersion)
            {
                throw new InvalidOperationException("Unexpected durable version.");
            }

            currentVersion = request.NextDurableVersion;
            return Task.FromResult<DurableProjectSaveRepositoryOutcome>(
                new DurableProjectSaveStored(
                    request.NextDurableVersion,
                    request.ProjectRevision.RevisionId));
        }

        public Task<DurableProjectSaveRepositoryOutcome?> TryReadSaveReceiptAsync(
            DurableProjectSaveRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<DurableProjectSaveRepositoryOutcome?>(null);
        }
    }

    private sealed class BlockingWorkspace : DelegatingEditorWorkspace
    {
        private readonly TaskCompletionSource release = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource started = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int openCount;

        public Task Started => started.Task;

        public int OpenCount => Volatile.Read(ref openCount);

        public override async Task<WorkspaceOpenOutcome> OpenAsync(
            OpenWorkspaceRequest request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref openCount);
            started.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            return await base.OpenAsync(request, cancellationToken);
        }

        public void Release() => release.TrySetResult();
    }

    private sealed class PreparedExportWorkspace : DelegatingEditorWorkspace
    {
        public WorkspaceCommand? Command { get; private set; }

        public override Task<WorkspaceCommandOutcome> DispatchAsync(
            WorkspaceCommand command,
            CancellationToken cancellationToken)
        {
            if (command is not PrepareExport prepare)
            {
                return base.DispatchAsync(command, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            Command = command;
            return Task.FromResult<WorkspaceCommandOutcome>(new ExportPrepared(
                prepare.Precondition.ProjectRevisionId,
                new ExportTicket("export-ticket-component-0001"),
                300));
        }
    }

    private sealed class BlockingCompilationObservationWorkspace : DelegatingEditorWorkspace
    {
        private readonly TaskCompletionSource observationStarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource releaseObservation = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private CompilationGeneration? acceptedGeneration;

        public CancellationToken ObservationCancellationToken { get; private set; }

        public Task ObservationStarted => observationStarted.Task;

        public void ReleaseObservation() => releaseObservation.TrySetResult();

        public override async Task<WorkspaceCommandOutcome> DispatchAsync(
            WorkspaceCommand command,
            CancellationToken cancellationToken)
        {
            var outcome = await base.DispatchAsync(command, cancellationToken);
            if (command is RequestCompilation && outcome is CompilationAccepted accepted)
            {
                acceptedGeneration = accepted.CompilationGeneration;
            }

            return outcome;
        }

        public override async Task<WorkspaceReadOutcome> ReadAsync(
            WorkspaceQueryContext context,
            WorkspaceQuery query,
            CancellationToken cancellationToken)
        {
            var outcome = await base.ReadAsync(context, query, cancellationToken);
            if (acceptedGeneration is not { } generation)
            {
                return outcome;
            }

            var projectionRead = query is ReadProjection
                ? (ProjectionSnapshot)outcome
                : (ProjectionSnapshot)await base.ReadAsync(
                    context,
                    ReadProjection.Instance,
                    cancellationToken);
            var projection = projectionRead.Projection;
            if (query is ReadCompilation)
            {
                ObservationCancellationToken = cancellationToken;
                observationStarted.TrySetResult();
                await releaseObservation.Task.WaitAsync(cancellationToken);
            }

            var compilation = new CompilationQueuedProjection(generation);
            return query is ReadCompilation
                ? new CompilationSnapshot(compilation, projection.ProjectionVersion)
                : new ProjectionSnapshot(projection with
                {
                    Compilation = compilation,
                });
        }
    }

    private sealed class TypedCancellationCompilationObservationWorkspace
        : DelegatingEditorWorkspace
    {
        private readonly TaskCompletionSource observationStarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource releaseObservation = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource cancellationStarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ManualResetEventSlim allowCancellationToComplete = new();
        private CompilationGeneration? acceptedGeneration;
        private int detachCount;

        public int DetachCount => Volatile.Read(ref detachCount);

        public Task CancellationStarted => cancellationStarted.Task;

        public Task ObservationStarted => observationStarted.Task;

        public void AllowCancellationToComplete() => allowCancellationToComplete.Set();

        public void ReleaseObservation() => releaseObservation.TrySetResult();

        public override async ValueTask DisposeAsync()
        {
            allowCancellationToComplete.Set();
            try
            {
                await base.DisposeAsync();
            }
            finally
            {
                allowCancellationToComplete.Dispose();
            }
        }

        public override async Task<WorkspaceCommandOutcome> DispatchAsync(
            WorkspaceCommand command,
            CancellationToken cancellationToken)
        {
            var outcome = await base.DispatchAsync(command, cancellationToken);
            if (command is RequestCompilation && outcome is CompilationAccepted accepted)
            {
                acceptedGeneration = accepted.CompilationGeneration;
            }

            return outcome;
        }

        public override async Task<WorkspaceReadOutcome> ReadAsync(
            WorkspaceQueryContext context,
            WorkspaceQuery query,
            CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return CancelledRead();
            }

            var outcome = await base.ReadAsync(context, query, cancellationToken);
            if (acceptedGeneration is not { } generation)
            {
                return outcome;
            }

            if (query is ReadCompilation)
            {
                observationStarted.TrySetResult();
                // Disposal of this registration would wait for the deliberately blocked
                // callback, preventing the observation from returning its cancelled outcome.
                _ = cancellationToken.Register(() =>
                {
                    cancellationStarted.TrySetResult();
                    allowCancellationToComplete.Wait();
                });
                await releaseObservation.Task;

                return CancelledRead();
            }

            return outcome;
        }

        public override Task<WorkspaceDetachOutcome> DetachAsync(
            DetachRequest request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref detachCount);
            return base.DetachAsync(request, cancellationToken);
        }

        private static WorkspaceReadRejected CancelledRead()
        {
            return new WorkspaceReadRejected(
                "workspace_cancelled",
                [],
                RetryDisposition.RefreshProjection);
        }
    }

    private sealed class BlockingCompilationWorkspace : DelegatingEditorWorkspace
    {
        private readonly TaskCompletionSource compilationDispatchStarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource releaseCompilation = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public CancellationToken CompilationCancellationToken { get; private set; }

        public Task CompilationDispatchStarted => compilationDispatchStarted.Task;

        public void AcceptCompilation() => releaseCompilation.TrySetResult();

        public override async Task<WorkspaceCommandOutcome> DispatchAsync(
            WorkspaceCommand command,
            CancellationToken cancellationToken)
        {
            if (command is not RequestCompilation request)
            {
                return await base.DispatchAsync(command, cancellationToken);
            }

            CompilationCancellationToken = cancellationToken;
            compilationDispatchStarted.TrySetResult();
            await releaseCompilation.Task;
            return new CompilationAccepted(
                new CompilationGeneration(1),
                request.Precondition.ProjectRevisionId,
                1);
        }
    }

    private sealed class FailingStepWorkspace : DelegatingEditorWorkspace
    {
        public override Task<WorkspaceCommandOutcome> DispatchAsync(
            WorkspaceCommand command,
            CancellationToken cancellationToken)
        {
            return command is StepSession
                ? Task.FromResult<WorkspaceCommandOutcome>(new SessionAdvanceFailed(
                    sessionVersion: 1,
                    logicalTime: 0,
                    new AdvanceFailureProjection(
                        AdvanceFailureReason.SimulationInternalDefect,
                        [],
                        policyEvidence: null),
                    projectionVersion: 1))
                : base.DispatchAsync(command, cancellationToken);
        }
    }

    private sealed class RecoveringWorkspace : DelegatingEditorWorkspace
    {
        private int openCount;

        public int OpenCount => Volatile.Read(ref openCount);

        public override Task<WorkspaceOpenOutcome> OpenAsync(
            OpenWorkspaceRequest request,
            CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref openCount) == 1)
            {
                return Task.FromResult<WorkspaceOpenOutcome>(
                    new WorkspaceOpenRejected(
                        "workspace_internal_defect",
                        [],
                        RetryDisposition.DoNotRetry));
            }

            return base.OpenAsync(request, cancellationToken);
        }
    }

    private sealed class ExpiringWorkspace : DelegatingEditorWorkspace
    {
        private int isExpired;
        private int openCount;

        public int OpenCount => Volatile.Read(ref openCount);

        public override Task<WorkspaceOpenOutcome> OpenAsync(
            OpenWorkspaceRequest request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref openCount);
            Volatile.Write(ref isExpired, 0);
            return base.OpenAsync(request, cancellationToken);
        }

        public override Task<WorkspaceCommandOutcome> DispatchAsync(
            WorkspaceCommand command,
            CancellationToken cancellationToken)
        {
            Volatile.Write(ref isExpired, 1);
            return Task.FromResult<WorkspaceCommandOutcome>(
                new WorkspaceCommandRejected(
                    "workspace_expired",
                    [],
                    RetryDisposition.DoNotRetry));
        }

        public override Task<WorkspaceReadOutcome> ReadAsync(
            WorkspaceQueryContext context,
            WorkspaceQuery query,
            CancellationToken cancellationToken)
        {
            return Volatile.Read(ref isExpired) == 0
                ? base.ReadAsync(context, query, cancellationToken)
                : Task.FromResult<WorkspaceReadOutcome>(
                    new WorkspaceReadRejected(
                        "workspace_not_found",
                        [],
                        RetryDisposition.DoNotRetry));
        }
    }

    private sealed class BlockingAuthorWorkspace : DelegatingEditorWorkspace
    {
        private readonly TaskCompletionSource release = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource started = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int dispatchCount;

        public Task Started => started.Task;

        public int DispatchCount => Volatile.Read(ref dispatchCount);

        public override async Task<WorkspaceCommandOutcome> DispatchAsync(
            WorkspaceCommand command,
            CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref dispatchCount) == 2)
            {
                started.TrySetResult();
                await release.Task.WaitAsync(cancellationToken);
            }

            return await base.DispatchAsync(command, cancellationToken);
        }

        public void Release() => release.TrySetResult();
    }

    private class TrackingWorkspace(WorkspacePolicy? workspacePolicy = null)
        : DelegatingEditorWorkspace(workspacePolicy)
    {
        private Attached? attachment;
        private int attachCount;
        private int detachCount;
        private WorkspaceId? workspaceId;
        private int dispatchCount;
        private int openCount;
        private int readCount;

        public int DispatchCount => Volatile.Read(ref dispatchCount);

        public int AttachCount => Volatile.Read(ref attachCount);

        public int DetachCount => Volatile.Read(ref detachCount);

        public int OpenCount => Volatile.Read(ref openCount);

        public int ReadCount => Volatile.Read(ref readCount);

        public override async Task<WorkspaceOpenOutcome> OpenAsync(
            OpenWorkspaceRequest request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref openCount);
            var outcome = await base.OpenAsync(request, cancellationToken);
            if (outcome is WorkspaceOpened opened)
            {
                workspaceId = opened.WorkspaceId;
            }

            return outcome;
        }

        public override Task<WorkspaceCommandOutcome> DispatchAsync(
            WorkspaceCommand command,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref dispatchCount);
            return base.DispatchAsync(command, cancellationToken);
        }

        public override async Task<WorkspaceAttachOutcome> AttachAsync(
            AttachRequest request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref attachCount);
            var outcome = await base.AttachAsync(request, cancellationToken);
            if (outcome is Attached attached)
            {
                attachment = attached;
            }

            return outcome;
        }

        public override async Task<WorkspaceDetachOutcome> DetachAsync(
            DetachRequest request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref detachCount);
            var outcome = await base.DetachAsync(request, cancellationToken);
            if (outcome is Detached)
            {
                attachment = null;
            }

            return outcome;
        }

        public override Task<WorkspaceReadOutcome> ReadAsync(
            WorkspaceQueryContext context,
            WorkspaceQuery query,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref readCount);
            return base.ReadAsync(context, query, cancellationToken);
        }

        public async Task<WorkspaceProjection> ReadCurrent()
        {
            var currentWorkspaceId = workspaceId
                ?? throw new InvalidOperationException("Workspace is not open.");
            var currentAttachment = attachment
                ?? throw new InvalidOperationException("Workspace is not attached.");
            var outcome = await base.ReadAsync(
                new WorkspaceQueryContext(
                    currentWorkspaceId,
                    currentAttachment.AttachmentId,
                    currentAttachment.Generation,
                    AnonymousWorkspaceCaller.Instance),
                ReadProjection.Instance,
                CancellationToken.None);
            return ((ProjectionSnapshot)outcome).Projection;
        }

    }
}
