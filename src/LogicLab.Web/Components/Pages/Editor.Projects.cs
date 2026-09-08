using System.Diagnostics;
using LogicLab.Application.Workspaces;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace LogicLab.Web.Components.Pages;

public sealed partial class Editor
{
    private void UpdateClaimDisplayName(string value)
    {
        ClaimDisplayName = value;
    }

    private async Task ClaimSandboxProject()
    {
        var projection = Projection
            ?? throw new InvalidOperationException("A Workspace is not attached.");
        var outcome = await Execute(context => new ClaimSandbox(
            context,
            new ClaimPrecondition(projection.ProjectRevision.RevisionId),
            ClaimDisplayName));
        Status = outcome switch
        {
            DurableProjectClaimed claimed => Text[
                "ClaimSucceeded",
                claimed.DisplayName.Value],
            WorkspaceCommandRejected rejected => Text["ClaimRejected", rejected.Code],
            _ => throw new UnreachableException(),
        };
    }

    private async Task SaveDurableProject()
    {
        var projection = Projection
            ?? throw new InvalidOperationException("A Workspace is not attached.");
        var durability = projection.Durability as DurableWorkspaceDurabilityProjection
            ?? throw new InvalidOperationException("The Workspace is not durable.");
        var outcome = await Execute(context => new SaveDurable(
            context,
            new DurableSavePrecondition(
                projection.ProjectRevision.RevisionId,
                durability.ObservedDurableVersion)));
        Status = outcome switch
        {
            DurableProjectSaved saved => Text[
                "SaveSucceeded",
                saved.DurableVersion.Value],
            DurableProjectSaveConflict => Text["SaveConflictStatus"],
            WorkspaceCommandRejected rejected => Text["SaveRejected", rejected.Code],
            _ => throw new UnreachableException(),
        };
    }

    private async Task ReloadDurableProject()
    {
        var durability = Projection?.Durability as DurableWorkspaceDurabilityProjection
            ?? throw new InvalidOperationException("The Workspace is not durable.");
        await OpenIndependentWorkspace(
            new OpenDurable(durability.DurableProjectId, RequireCurrentCaller()),
            Text["OpeningLatestDurable"]);
    }

    private async Task KeepConflictAsCopy()
    {
        var attachment = Attachment
            ?? throw new InvalidOperationException("A Workspace is not attached.");
        var projection = Projection
            ?? throw new InvalidOperationException("A Workspace is not attached.");
        await OpenIndependentWorkspace(
            new CopyWorkspace(
                projection.WorkspaceId,
                attachment.AttachmentId,
                attachment.Generation,
                projection.ProjectionVersion,
                WorkspaceCopySaveTarget.DetachedSandbox,
                RequireCurrentCaller()),
            Text["OpeningCopy"]);
    }

    private async Task OpenIndependentWorkspace(
        OpenWorkspaceRequest request,
        string openingStatus)
    {
        Status = openingStatus;
        var outcome = await workspace.OpenAsync(request, componentCancellationToken);
        if (outcome is WorkspaceOpenRejected rejected)
        {
            Status = Text["OpeningRejected", rejected.Code];
            return;
        }

        var opened = (WorkspaceOpened)outcome;
        Navigation.NavigateTo(
            CreateWorkspaceLocator(opened.WorkspaceId),
            new NavigationOptions
            {
                ForceLoad = true,
                ReplaceHistoryEntry = true,
            });
    }

    private async Task PrepareProjectExport()
    {
        var revision = Projection?.ProjectRevision
            ?? throw new InvalidOperationException("Workspace is not open.");
        var revisionId = revision.RevisionId;
        var outcome = await Execute(context => new PrepareExport(
            context,
            new AuthoringPrecondition(revisionId),
            revisionId));
        if (outcome is not ExportPrepared prepared)
        {
            PreparedExportUrl = null;
            Status = Text[
                "ExportRejected",
                ((WorkspaceCommandRejected)outcome).Code];
            return;
        }

        PreparedExportUrl =
            $"/downloads/{Uri.EscapeDataString(prepared.ExportTicket.Value)}";
        Status = Text["ExportPrepared", prepared.ExpiresAfterSeconds];
    }

    private async Task ImportProjectPackage(InputFileChangeEventArgs change)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (ActiveCommand is not null || !CanImport)
        {
            return;
        }

        ActiveCommand = "import";
        PreparedExportUrl = null;
        try
        {
            await using var source = change.File.OpenReadStream(
                projectImportWorkflow.MaximumCarrierBytes,
                componentCancellationToken);
            var outcome = await projectImportWorkflow.ImportAsync(
                source,
                RequireCurrentCaller(),
                componentCancellationToken);
            if (outcome is WorkspaceOpenRejected rejected)
            {
                Status = Text["ImportRejected", rejected.Code];
                return;
            }

            var imported = (WorkspaceOpened)outcome;
            Status = Text["ImportOpening"];
            Navigation.NavigateTo(
                CreateWorkspaceLocator(imported.WorkspaceId),
                new NavigationOptions
                {
                    ForceLoad = true,
                    ReplaceHistoryEntry = true,
                });
        }
        catch (OperationCanceledException)
            when (componentLifetime.IsCancellationRequested)
        {
            Status = Text["ImportCancelled"];
        }
        catch (IOException)
        {
            Status = Text["ImportRejected", "package_limit_exceeded"];
        }
        finally
        {
            ActiveCommand = null;
        }
    }
}
