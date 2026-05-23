using System.Threading;
using System.Threading.Tasks;

namespace FrameShift.Core.Actions;

public sealed class MediaInfoAction : IFrameShiftAction
{
    public ActionDescriptor Descriptor { get; } = new(
        "media-info",
        "Media Info",
        "Affiche les métadonnées techniques du fichier média sélectionné.");

    public Task<ActionExecutionResult> ExecuteAsync(ActionRequest request, CancellationToken cancellationToken)
    {
        return Task.FromResult(new ActionExecutionResult(
            false,
            "Media Info nécessite une interface graphique et ne peut pas être exécuté en mode headless."));
    }
}
