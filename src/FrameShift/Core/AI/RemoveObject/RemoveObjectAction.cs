using System.Threading;
using System.Threading.Tasks;
using FrameShift.Core.Actions;

namespace FrameShift.Core.AI.RemoveObject;

internal sealed class RemoveObjectAction : IFrameShiftAction
{
    public ActionDescriptor Descriptor { get; } = new(
        "remove-object",
        L.Get("RemoveObjectAction_Remove_Object_69174617"),
        L.Get("RemoveObjectAction_Supprime_un_objet_d_une_image_via_in_cd8a7de2"));

    public Task<ActionExecutionResult> ExecuteAsync(ActionRequest request, CancellationToken cancellationToken)
    {
        // UI-first action: intercepted in Program.RunCli before reaching the queue.
        return Task.FromResult(new ActionExecutionResult(
            false,
            L.Get("RemoveObjectAction_Remove_Object_requires_the_visual_ed_01e34c81")));
    }
}
