using System.Threading;
using System.Threading.Tasks;

namespace FrameShift.Core.Actions;

public sealed class MediaInfoAction : IFrameShiftAction
{
    public ActionDescriptor Descriptor { get; } = new(
        "media-info",
        L.Get("MediaInfoAction_Media_Info_8e2e9556"),
        L.Get("MediaInfoAction_Displays_the_technical_metadata_of_t_7a1e9fb4"));

    public Task<ActionExecutionResult> ExecuteAsync(ActionRequest request, CancellationToken cancellationToken)
    {
        return Task.FromResult(new ActionExecutionResult(
            false,
            L.Get("MediaInfoAction_Media_Info_requires_a_graphical_inte_4566d517")));
    }
}
