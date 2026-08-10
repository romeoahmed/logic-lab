using System.Security.Claims;
using Bunit;
using LogicLab.Application.Workspaces;
using LogicLab.Web.Components.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;

namespace LogicLab.Web.Tests;

internal sealed class EditorDurableRouteTests
{
    [Test]
    public async Task Editor_DurableRoute_UsesCurrentSubjectForAttachmentLifetime()
    {
        await using var context = new BunitContext();
        await using var workspace = new RecordingAttachWorkspace();
        var opened = (WorkspaceOpened)await workspace.OpenAsync(
            new CreateSandbox("Reopened project", "Main"),
            CancellationToken.None);
        context.Services.AddFluentUIComponents();
        context.Services.AddSingleton(TimeProvider.System);
        context.Services.AddSingleton<IEditorWorkspace>(workspace);
        context.Renderer.SetRendererInfo(new RendererInfo("Server", isInteractive: true));
        var authenticationState = Task.FromResult(new AuthenticationState(
            new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "subject-editor")],
                authenticationType: "Tests"))));

        var rendered = context.Render<CascadingValue<Task<AuthenticationState>>>(
            parameters => parameters
                .Add(value => value.Value, authenticationState)
                .AddChildContent<Editor>(editor => editor
                    .Add(component => component.WorkspaceIdValue,
                        opened.WorkspaceId.Value)));
        await rendered.WaitForStateAsync(() => workspace.Request is not null);
        await Assert.That(rendered.Markup).Contains("Durable Project reopened.");
        await rendered.Find("[data-command='author']").ClickAsync();
        await rendered.WaitForStateAsync(() => workspace.ReadCaller is not null);
        await rendered.FindComponent<Editor>().Instance.DisposeAsync();

        var attach = (await Assert.That(workspace.Request)
            .IsTypeOf<InitialAttach>())!;
        using (Assert.Multiple())
        {
            await Assert.That(((AuthenticatedWorkspaceCaller)attach.Caller)
                    .SubjectId.Value)
                .IsEqualTo("subject-editor");
            await Assert.That(((AuthenticatedWorkspaceCaller)workspace.CommandCaller!)
                    .SubjectId.Value)
                .IsEqualTo("subject-editor");
            await Assert.That(((AuthenticatedWorkspaceCaller)workspace.ReadCaller!)
                    .SubjectId.Value)
                .IsEqualTo("subject-editor");
            await Assert.That(((AuthenticatedWorkspaceCaller)workspace.DetachCaller!)
                    .SubjectId.Value)
                .IsEqualTo("subject-editor");
        }
    }

    private sealed class RecordingAttachWorkspace : DelegatingEditorWorkspace
    {
        public AttachRequest? Request { get; private set; }

        public WorkspaceCaller? CommandCaller { get; private set; }

        public WorkspaceCaller? ReadCaller { get; private set; }

        public WorkspaceCaller? DetachCaller { get; private set; }

        public override Task<WorkspaceAttachOutcome> AttachAsync(
            AttachRequest request,
            CancellationToken cancellationToken)
        {
            Request = request;
            return base.AttachAsync(request, cancellationToken);
        }

        public override Task<WorkspaceCommandOutcome> DispatchAsync(
            WorkspaceCommand command,
            CancellationToken cancellationToken)
        {
            CommandCaller = command.Context.Caller;
            return base.DispatchAsync(command, cancellationToken);
        }

        public override Task<WorkspaceReadOutcome> ReadAsync(
            WorkspaceQueryContext context,
            WorkspaceQuery query,
            CancellationToken cancellationToken)
        {
            ReadCaller = context.Caller;
            return base.ReadAsync(context, query, cancellationToken);
        }

        public override Task<WorkspaceDetachOutcome> DetachAsync(
            DetachRequest request,
            CancellationToken cancellationToken)
        {
            DetachCaller = request.Caller;
            return base.DetachAsync(request, cancellationToken);
        }
    }
}
