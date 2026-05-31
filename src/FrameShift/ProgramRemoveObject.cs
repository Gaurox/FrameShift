using System.IO;
using System.Windows.Forms;
using FrameShift.Core.Actions;
using FrameShift.Core.Logging;
using FrameShift.Windows.AI;

namespace FrameShift;

internal static partial class Program
{
    private static int RunRemoveObject(string inputPath, AppLogger logger)
    {
        if (!File.Exists(inputPath))
        {
            ShowCliError(MediaActionMessages.InputFileNotFound(inputPath));
            return 1;
        }

        var ext = Path.GetExtension(inputPath).ToLowerInvariant();
        if (ext is not (".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp"))
        {
            ShowCliError(MediaActionMessages.UnsupportedSourceFormat(ext, ".png, .jpg, .jpeg, .webp, .bmp"));
            return 1;
        }

        logger.Log($"Program: opening RemoveObjectEditorForm for '{inputPath}'.");
        using var form = new RemoveObjectEditorForm(inputPath, logger);
        form.ShowDialog();
        return 0;
    }
}
