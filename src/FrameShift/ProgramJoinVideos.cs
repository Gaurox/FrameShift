using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using FrameShift.Core.Actions;
using FrameShift.Core.FFmpeg;
using FrameShift.Core.FFprobe;
using FrameShift.Core.Helpers;
using FrameShift.Core.Logging;
using FrameShift.Windows.Forms;

namespace FrameShift;

internal static partial class Program
{
    private static readonly string JoinVideosMutexName = LocalPipeTransport.Name(@"Local\FrameShift_JoinVideos");
    private static readonly string JoinVideosPipeName = LocalPipeTransport.Name("FrameShift_JoinVideosQueue");

    private static int RunJoinVideos(
        IFrameShiftAction action,
        IReadOnlyList<string> inputPaths,
        Dictionary<string, string> options,
        AppLogger logger)
    {
        if (!ValidateJoinVideosInputs(inputPaths))
        {
            return 1;
        }

        if (options.ContainsKey(ActionOptionKeys.JoinVideosSettings))
        {
            if (inputPaths.Count < 2)
            {
                ShowCliError(MediaActionMessages.JoinVideosRequiresAtLeastTwoVideos());
                return 1;
            }

            return RunQueuedActionWithProgressForm(action, [inputPaths[0]], options, logger, "join-videos");
        }

        if (options.TryGetValue(ActionOptionKeys.JoinVideosMode, out var modeText))
        {
            if (inputPaths.Count < 2)
            {
                ShowCliError(MediaActionMessages.JoinVideosRequiresAtLeastTwoVideos());
                return 1;
            }

            if (!JoinVideosSettings.TryParseMode(modeText, out var mode))
            {
                ShowCliError("The --join-mode option requires one of: auto, copy, normalize.");
                return 1;
            }

            options[ActionOptionKeys.JoinVideosSettings] = new JoinVideosSettings
            {
                InputPaths = [.. inputPaths],
                Mode = mode
            }.ToOptionPayload();
            return RunQueuedActionWithProgressForm(action, [inputPaths[0]], options, logger, "join-videos");
        }

        using var mutex = new Mutex(true, JoinVideosMutexName, out var isPrimary);
        if (!isPrimary)
        {
            return SendJoinVideosPathsToPrimary(inputPaths, logger);
        }

        using var cancellationSource = new CancellationTokenSource();
        JoinVideosForm? form = null;
        Thread? listenerThread = null;
        try
        {
            var toolLocator = new ToolLocator();
            form = new JoinVideosForm(
                inputPaths,
                toolLocator.ResolveFfmpegPath(),
                toolLocator.ResolveFfprobePath(),
                new FfmpegRunner(logger),
                new FfprobeRunner(logger));

            _ = form.Handle;
            listenerThread = new Thread(() => RunJoinVideosPipeListener(form, cancellationSource.Token))
            {
                IsBackground = true
            };
            listenerThread.Start();

            var dialogResult = form.ShowDialog();
            cancellationSource.Cancel();
            listenerThread.Join(TimeSpan.FromSeconds(1));
            if (dialogResult != System.Windows.Forms.DialogResult.OK || form.Settings is null)
            {
                return 0;
            }

            options[ActionOptionKeys.JoinVideosSettings] = form.Settings.ToOptionPayload();
            return RunQueuedActionWithProgressForm(action, [form.Settings.InputPaths[0]], options, logger, "join-videos");
        }
        catch (Exception ex)
        {
            logger.Log($"Program: Join Videos startup failed: {ex}");
            ShowCliError(ConversionActionHelper.GetFriendlyExceptionMessage(ex, MediaActionMessages.JoinVideosFailed()));
            return 1;
        }
        finally
        {
            cancellationSource.Cancel();
            listenerThread?.Join(TimeSpan.FromSeconds(1));
            form?.Dispose();
        }
    }

    private static bool ValidateJoinVideosInputs(IReadOnlyList<string> inputPaths)
    {
        foreach (var inputPath in inputPaths)
        {
            var extension = Path.GetExtension(inputPath).ToLowerInvariant();
            if (extension is not (".mp4" or ".mkv" or ".avi" or ".mov" or ".webm" or ".m4v"))
            {
                ShowCliError(MediaActionMessages.UnsupportedSourceFormat(extension, ".mp4, .mkv, .avi, .mov, .webm, .m4v"));
                return false;
            }

            if (!File.Exists(inputPath))
            {
                ShowCliError(MediaActionMessages.InputFileNotFound(inputPath));
                return false;
            }
        }

        return true;
    }

    private static int SendJoinVideosPathsToPrimary(IReadOnlyList<string> inputPaths, AppLogger logger)
    {
        try
        {
            LocalPipeTransport.SendPathsAsync(JoinVideosPipeName, inputPaths).GetAwaiter().GetResult();

            return 0;
        }
        catch (Exception ex)
        {
            logger.Log($"Program: Join Videos failed to send to primary instance: {ex}");
            ShowCliError("FrameShift is already preparing Join Videos, but the new videos could not be added.");
            return 1;
        }
    }

    private static void RunJoinVideosPipeListener(JoinVideosForm form, CancellationToken cancellationToken)
    {
        LocalPipeTransport.ListenForPathsAsync(JoinVideosPipeName,
            paths => { if (!form.IsDisposed) form.AddPathsThreadSafe(paths); }, cancellationToken).GetAwaiter().GetResult();
    }
}
