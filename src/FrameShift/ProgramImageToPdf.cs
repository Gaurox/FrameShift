using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;
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
    private const string ImageToPdfMutexName = @"Local\FrameShift_ImageToPdf";
    private const string ImageToPdfPipeName = "FrameShift_ImageToPdfQueue";

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
                using var pipe = new NamedPipeClientStream(".", ImageToPdfPipeName, PipeDirection.Out);
                pipe.Connect(5000);
                using var writer = new StreamWriter(pipe, Encoding.UTF8) { AutoFlush = true };
                foreach (var path in inputPaths)
                {
                    writer.WriteLine(path);
                }

                return 0;
            }
            catch (Exception ex)
            {
                logger.Log($"Program: RunImageToPdf failed to send to primary instance: {ex}");
                ShowCliError("FrameShift is already running, but the new image could not be added.");
                return 1;
            }
        }

        using var cts = new CancellationTokenSource();
        ImageToPdfForm? form = null;
        try
        {
            var toolLocator = new ToolLocator();
            var ffmpegPath = toolLocator.ResolveFfmpegPath();
            form = new ImageToPdfForm(inputPaths, ffmpegPath, new FfmpegRunner(logger));

            var listenerThread = new Thread(() => RunImageToPdfPipeListener(form, cts.Token))
            {
                IsBackground = true
            };
            listenerThread.Start();

            if (form.ShowDialog() != DialogResult.OK || form.Settings is null)
            {
                return 0;
            }

            options[ActionOptionKeys.ImageToPdfSettings] = form.Settings.ToOptionPayload();
            return RunImmediateAction(action, inputPaths[0], options, logger);
        }
        catch (Exception ex)
        {
            ShowCliError(ConversionActionHelper.GetFriendlyExceptionMessage(ex, MediaActionMessages.Failed("Image to PDF")));
            return 0;
        }
        finally
        {
            cts.Cancel();
            form?.Dispose();
        }
    }

    private static void RunImageToPdfPipeListener(ImageToPdfForm form, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = new NamedPipeServerStream(
                    ImageToPdfPipeName,
                    PipeDirection.In,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                pipe.WaitForConnectionAsync(cancellationToken).GetAwaiter().GetResult();

                if (cancellationToken.IsCancellationRequested)
                {
                    pipe.Dispose();
                    break;
                }

                using (pipe)
                {
                    using var reader = new StreamReader(pipe, Encoding.UTF8);
                    var paths = new List<string>();
                    string? line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (!string.IsNullOrWhiteSpace(line))
                        {
                            paths.Add(line);
                        }
                    }

                    if (paths.Count > 0 && !form.IsDisposed)
                    {
                        form.AddPathsThreadSafe(paths);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                pipe?.Dispose();
                break;
            }
            catch
            {
                pipe?.Dispose();
            }
        }
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
            ShowCliError(ConversionActionHelper.GetFriendlyExceptionMessage(ex, MediaActionMessages.Failed("Image to PDF")));
            return false;
        }
    }
}
