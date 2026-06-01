using System.Threading;
using System.Threading.Tasks;

namespace FrameShift.Core.Actions;

public sealed class MediaInfoAction : IFrameShiftAction
{
    public ActionDescriptor Descriptor { get; } = new(
        "media-info",
        "Media Info",
        "Displays the technical metadata of the selected media file.");

    public Task<ActionExecutionResult> ExecuteAsync(ActionRequest request, CancellationToken cancellationToken)
    {
        return Task.FromResult(new ActionExecutionResult(
            false,
            "Media Info requires a graphical interface and cannot be run in headless mode."));
    }
}
