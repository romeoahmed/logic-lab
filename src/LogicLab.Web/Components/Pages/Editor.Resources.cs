using LogicLab.Domain.Authoring;
using LogicLab.Web.Components.Editor;

namespace LogicLab.Web.Components.Pages;

public sealed partial class Editor
{
    private bool showMemoryImages;
    private MemoryImageId? selectedMemoryImageId;

    private Task HandleMemoryEditAsync(MemoryImageEditor.EditRequest request) => RunCommandAsync(
        "memory-edit", () => showMemoryImages && CanMutateWorkspace && Projection?.ProjectRevision.RevisionId == request.RevisionId,
        async () =>
        {
            var previousIds = Projection!.ProjectRevision.Document.MemoryImages.Select(image => image.Id).ToHashSet();
            if (!await Apply(request.Intent))
            {
                return;
            }
            if (request.Intent is CreateMemoryImageIntent)
            {
                selectedMemoryImageId = Projection!.ProjectRevision.Document.MemoryImages.Single(image => !previousIds.Contains(image.Id)).Id;
            }
            else if (request.Intent is RemoveMemoryImageIntent removed && selectedMemoryImageId == removed.MemoryImageId)
            {
                selectedMemoryImageId = null;
            }
            Status = Text["MemoryImageEditApplied"];
        });
}
