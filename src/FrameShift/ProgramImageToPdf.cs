using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using FrameShift.Core.Actions;
using FrameShift.Core.FFmpeg;
using FrameShift.Core.Helpers;
using FrameShift.Core.Logging;
using FrameShift.Windows.Forms;

namespace FrameShift;

internal static partial class Program
{
    private static readonly string ImageToPdfMutexName = LocalPipeTransport.Name(@"Local\FrameShift_ImageToPdf");
    private static readonly string ImageToPdfPipeName = LocalPipeTransport.Name("FrameShift_ImageToPdfQueue");

    private static int RunImageToPdf(
        IFrameShiftAction action,
        IReadOnlyList<string> inputPaths,
        Dictionary<string, string> options,
        AppLogger logger)
    {
        if (options.ContainsKey(ActionOptionKeys.ImageToPdfSettings))
        {
            return RunImmediateAction(action, inputPaths[0], options, logger);
        }

        if (inputPaths.Count == 0)
        {
            ShowCliError(MediaActionMessages.InputPathRequired());
            return 1;
        }

        foreach (var inputPath in inputPaths)
        {
            var ext = Path.GetExtension(inputPath).ToLowerInvariant();
            if (ext is not (".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp"))
            {
                ShowCliError(MediaActionMessages.UnsupportedSourceFormat(ext, ImageCropSupport.GetSupportedExtensionsText()));
                return 1;
            }
        }

        using var mutex = new Mutex(true, ImageToPdfMutexName, out var isPrimary);

        if (!isPrimary)
        {
            try
            {
                LocalPipeTransport.SendPathsAsync(ImageToPdfPipeName, inputPaths).GetAwaiter().GetResult();

                return 0;
            }
            catch (Exception ex)
            {
                logger.Log($"Program: RunImageToPdf failed to send to primary instance: {ex}");
                ShowCliError(L.Get("ProgramImageToPdf_FrameShift_is_already_running_but_th_c7923d22"));
                return 1;
            }
        }

        using var cts = new CancellationTokenSource();
        ImageToPdfForm? form = null;
        Thread? listenerThread = null;
        try
        {
            var toolLocator = new ToolLocator();
            var ffmpegPath = toolLocator.ResolveFfmpegPath();
            form = new ImageToPdfForm(inputPaths, ffmpegPath, new FfmpegRunner(logger));

            _ = form.Handle;
            listenerThread = new Thread(() => RunImageToPdfPipeListener(form, cts.Token))
            {
                IsBackground = true
            };
            listenerThread.Start();

            var dialogResult = form.ShowDialog();
            cts.Cancel();
            listenerThread.Join(TimeSpan.FromSeconds(1));
            if (dialogResult != DialogResult.OK || form.Settings is null)
            {
                return 0;
            }

            options[ActionOptionKeys.ImageToPdfSettings] = form.Settings.ToOptionPayload();
            return RunImmediateAction(action, inputPaths[0], options, logger);
        }
        catch (Exception ex)
        {
            ShowCliError(ConversionActionHelper.GetFriendlyExceptionMessage(ex, MediaActionMessages.Failed(L.Get("ProgramImageToPdf_Image_to_PDF_c9e73f3d"))));
            return 0;
        }
        finally
        {
            cts.Cancel();
            listenerThread?.Join(TimeSpan.FromSeconds(1));
            form?.Dispose();
        }
    }

    private static void RunImageToPdfPipeListener(ImageToPdfForm form, CancellationToken cancellationToken)
    {
        LocalPipeTransport.ListenForPathsAsync(ImageToPdfPipeName,
            paths => { if (!form.IsDisposed) form.AddPathsThreadSafe(paths); }, cancellationToken).GetAwaiter().GetResult();
    }

    private static bool EnsureImageToPdfOptions(List<string> inputPaths, Dictionary<string, string> options)
    {
        if (inputPaths.Count == 0)
        {
            ShowCliError(MediaActionMessages.InputPathRequired());
            return false;
        }

        foreach (var inputPath in inputPaths)
        {
            var sourceExtension = Path.GetExtension(inputPath).ToLowerInvariant();
            if (sourceExtension is not (".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp"))
            {
                ShowCliError(MediaActionMessages.UnsupportedSourceFormat(sourceExtension, ImageCropSupport.GetSupportedExtensionsText()));
                return false;
            }
        }

        if (options.ContainsKey(ActionOptionKeys.ImageToPdfSettings))
        {
            return true;
        }

        try
        {
            var toolLocator = new ToolLocator();
            var ffmpegPath = toolLocator.ResolveFfmpegPath();
            using var form = new ImageToPdfForm(inputPaths, ffmpegPath, new FfmpegRunner(new AppLogger()));
            if (form.ShowDialog() != DialogResult.OK || form.Settings is null)
            {
                return false;
            }

            options[ActionOptionKeys.ImageToPdfSettings] = form.Settings.ToOptionPayload();
            return true;
        }
        catch (Exception ex)
        {
            ShowCliError(ConversionActionHelper.GetFriendlyExceptionMessage(ex, MediaActionMessages.Failed(L.Get("ProgramImageToPdf_Image_to_PDF_c9e73f3d"))));
            return false;
        }
    }
}
