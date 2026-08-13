using LogicLab.Application.Workspaces;
using LogicLab.Web.Components.Pages;

namespace LogicLab.Web.Tests;

internal sealed class WorkspaceAttachmentHistoryStateTests
{
    [Test]
    [Arguments(
        "{\"Version\":1,\"WorkspaceId\":\"workspace-1\",\"AttachmentId\":\"attachment-1\",\"Generation\":1,\"Unexpected\":true}")]
    [Arguments(
        "{\"Version\":1,\"WorkspaceId\":\"workspace-1\",\"AttachmentId\":\"attachment-1\",\"Generation\":1,\"Generation\":2}")]
    public async Task TryRead_MalformedBrowserState_RejectsFence(string serialized)
    {
        var result = WorkspaceAttachmentHistoryState.TryRead(
            serialized,
            new WorkspaceId("workspace-1"),
            out var attachmentId,
            out var generation);

        using (Assert.Multiple())
        {
            await Assert.That(result).IsFalse();
            await Assert.That(attachmentId).IsNull();
            await Assert.That(generation).IsEqualTo(0UL);
        }
    }
}
