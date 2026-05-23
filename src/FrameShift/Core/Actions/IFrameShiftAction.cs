using System.Threading;
using System.Threading.Tasks;

namespace FrameShift.Core.Actions;

public interface IFrameShiftAction
{
    ActionDescriptor Descriptor { get; }

    Task<ActionExecutionResult> ExecuteAsync(ActionRequest request, CancellationToken cancellationToken);
}
