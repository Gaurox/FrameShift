using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;
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
    private const string JoinVideosMutexName = @"Local\FrameShift_JoinVideos";
    private const string JoinVideosPipeName = "FrameShift_JoinVideosQueue";

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
        try
        {
            var toolLocator = new ToolLocator();
            form = new JoinVideosForm(
                inputPaths,
                toolLocator.ResolveFfmpegPath(),
                toolLocator.ResolveFfprobePath(),
                new FfmpegRunner(logger),
                new FfprobeRunner(logger));

            var listenerThread = new Thread(() => RunJoinVideosPipeListener(form, cancellationSource.Token))
            {
                IsBackground = true
            };
            listenerThread.Start();

            if (form.ShowDialog() != System.Windows.Forms.DialogResult.OK || form.Settings is null)
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
            using var pipe = new NamedPipeClientStream(".", JoinVideosPipeName, PipeDirection.Out);
            pipe.Connect(5000);
            using var writer = new StreamWriter(pipe, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = true };
            foreach (var path in inputPaths)
            {
                writer.WriteLine(path);
            }

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
        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = new NamedPipeServerStream(
                    JoinVideosPipeName,
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
                using (var reader = new StreamReader(pipe, Encoding.UTF8))
                {
                    var paths = new List<string>();
                    string? line;
                    while ((line = reader.ReadLine()) is not null)
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
}
