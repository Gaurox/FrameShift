using System.Threading;
using System.Threading.Tasks;
using FrameShift.Core.Actions;

namespace FrameShift.Core.AI.RemoveObject;

internal sealed class RemoveObjectAction : IFrameShiftAction
{
    public ActionDescriptor Descriptor { get; } = new(
        "remove-object",
        "Remove Object",
        "Supprime un objet d'une image via inpainting IA local.");

    public Task<ActionExecutionResult> ExecuteAsync(ActionRequest request, CancellationToken cancellationToken)
    {
        // UI-first action: intercepted in Program.RunCli before reaching the queue.
        return Task.FromResult(new ActionExecutionResult(
            false,
            "Remove Object requires the visual editor. Launch via: FrameShift.exe --action remove-object \"<image>\""));
    }
}
