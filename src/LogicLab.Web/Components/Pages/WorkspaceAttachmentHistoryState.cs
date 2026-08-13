using System.Text.Json;
using LogicLab.Application.Workspaces;

namespace LogicLab.Web.Components.Pages;

internal sealed record WorkspaceAttachmentHistoryState(
    int Version,
    string WorkspaceId,
    string AttachmentId,
    ulong Generation)
{
    private const int CurrentVersion = 1;
    private const int MaximumSerializedLength = 512;
    private const int MaximumIdentifierLength = 64;

    public static string Serialize(Attached attachment)
    {
        ArgumentNullException.ThrowIfNull(attachment);
        return JsonSerializer.Serialize(new WorkspaceAttachmentHistoryState(
            CurrentVersion,
            attachment.Projection.WorkspaceId.Value,
            attachment.AttachmentId.Value,
            attachment.Generation));
    }

    public static bool TryRead(
        string? serialized,
        WorkspaceId expectedWorkspaceId,
        out WorkspaceAttachmentId? attachmentId,
        out ulong generation)
    {
        ArgumentNullException.ThrowIfNull(expectedWorkspaceId);
        attachmentId = null;
        generation = 0;
        if (string.IsNullOrEmpty(serialized)
            || serialized.Length > MaximumSerializedLength)
        {
            return false;
        }

        WorkspaceAttachmentHistoryState? state;
        try
        {
            state = JsonSerializer.Deserialize<WorkspaceAttachmentHistoryState>(serialized);
        }
        catch (JsonException)
        {
            return false;
        }

        if (state is null
            || state.Version != CurrentVersion
            || state.WorkspaceId != expectedWorkspaceId.Value
            || string.IsNullOrEmpty(state.AttachmentId)
            || state.AttachmentId.Length > MaximumIdentifierLength
            || state.Generation == 0)
        {
            return false;
        }

        attachmentId = new WorkspaceAttachmentId(state.AttachmentId);
        generation = state.Generation;
        return true;
    }
}
