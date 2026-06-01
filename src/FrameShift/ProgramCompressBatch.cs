using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using FrameShift.Core.Actions;
using FrameShift.Core.Logging;
using FrameShift.Windows.Forms;

namespace FrameShift;

internal static partial class Program
{
    private static readonly Dictionary<string, (string MutexName, string PipeName)> s_compressBatchInfo =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["compress-video"] = (@"Local\FrameShift_CompressVideoBatch", "FrameShift_CompressVideoBatchQueue"),
            ["compress-audio"] = (@"Local\FrameShift_CompressAudioBatch", "FrameShift_CompressAudioBatchQueue"),
            ["compress-image"] = (@"Local\FrameShift_CompressImageBatch", "FrameShift_CompressImageBatchQueue"),
        };

    private static int RunCompressBatch(string actionId, IFrameShiftAction action, List<string> inputPaths, AppLogger logger)
    {
        if (!s_compressBatchInfo.TryGetValue(actionId, out var info))
        {
            logger.Log($"RunCompressBatch: unknown actionId={actionId}");
            return 1;
        }

        using var batchMutex = new Mutex(true, info.MutexName, out var isPrimary);
        if (!isPrimary)
        {
            try
            {
                SendPathsToCompressPrimary(info.PipeName, inputPaths, logger);
                return 0;
            }
            catch (Exception ex)
            {
                logger.Log($"RunCompressBatch: SendPaths failed: {ex}");
                ShowCliError("FrameShift is already running, but the file could not be queued.");
                return 1;
            }
        }

        // Primary: collect all paths that arrive within the debounce window.
        var sync = new object();
        var collectedPaths = new List<string>(inputPaths.Where(p => !string.IsNullOrWhiteSpace(p)));
        var lastActivityTicks = new long[] { DateTime.UtcNow.Ticks };
        var pipeClosing = false;

        var pipeThread = new Thread(() =>
        {
            while (!pipeClosing)
            {
                try
                {
                    using var server = new NamedPipeServerStream(
                        info.PipeName,
                        PipeDirection.In,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);
                    server.WaitForConnection();
                    using var reader = new StreamReader(server, Encoding.UTF8);
                    string? line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (!string.IsNullOrWhiteSpace(line))
                        {
                            lock (sync)
                            {
                                if (!collectedPaths.Any(p => string.Equals(p, line, StringComparison.OrdinalIgnoreCase)))
                                    collectedPaths.Add(line);
                            }

                            Interlocked.Exchange(ref lastActivityTicks[0], DateTime.UtcNow.Ticks);
                            logger.Log($"RunCompressBatch ({actionId}): received path from secondary: {line}");
                        }
                    }
                }
                catch (Exception ex) when (!pipeClosing)
                {
                    logger.Log($"RunCompressBatch ({actionId}): pipe server error: {ex.Message}");
                }
            }
        })
        { IsBackground = true };

        pipeThread.Start();

        const int debounceMs = 700;
        while (true)
        {
            Thread.Sleep(50);
            var elapsed = (DateTime.UtcNow - new DateTime(Interlocked.Read(ref lastActivityTicks[0]), DateTimeKind.Utc)).TotalMilliseconds;
            if (elapsed >= debounceMs) break;
        }

        pipeClosing = true;

        List<string> paths;
        lock (sync)
        {
            paths = collectedPaths
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        logger.Log($"RunCompressBatch ({actionId}): debounce elapsed, pathCount={paths.Count}");

        if (paths.Count == 0)
            return 0;

        if (paths.Count == 1)
        {
            // Mono-file: original behavior — probe + form + run.
            var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var mono = new List<string> { paths[0] };
            var ok = actionId switch
            {
                "compress-video" => EnsureCompressVideoOptions(mono, options, logger),
                "compress-audio" => EnsureCompressAudioOptions(mono, options, logger),
                "compress-image" => EnsureCompressImageOptions(mono, options, logger),
                _ => false
            };
            if (!ok) return 0;
            return RunQueuedActionWithProgressForm(action, mono, options, logger, actionId);
        }

        // Multi-file: ask how to configure.
        CompressMultiFileChoice choice;
        using (var choiceForm = new CompressMultiFileChoiceForm(paths.Count))
        {
            if (choiceForm.ShowDialog() != DialogResult.OK)
                return 0;
            choice = choiceForm.Choice;
        }

        if (choice == CompressMultiFileChoice.SameForAll)
        {
            // Probe and show picker for the first file; apply those options to all.
            var sharedOptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var firstFile = new List<string> { paths[0] };
            var ok = actionId switch
            {
                "compress-video" => EnsureCompressVideoOptions(firstFile, sharedOptions, logger),
                "compress-audio" => EnsureCompressAudioOptions(firstFile, sharedOptions, logger),
                "compress-image" => EnsureCompressImageOptions(firstFile, sharedOptions, logger),
                _ => false
            };
            if (!ok) return 0;
            return RunQueuedActionWithProgressForm(action, paths, sharedOptions, logger, actionId);
        }
        else // PerFile
        {
            // Open picker for each file in turn; cancel on any dismissal.
            var requests = new List<ActionRequest>();
            foreach (var path in paths)
            {
                var fileOptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var singlePath = new List<string> { path };
                var ok = actionId switch
                {
                    "compress-video" => EnsureCompressVideoOptions(singlePath, fileOptions, logger),
                    "compress-audio" => EnsureCompressAudioOptions(singlePath, fileOptions, logger),
                    "compress-image" => EnsureCompressImageOptions(singlePath, fileOptions, logger),
                    _ => false
                };
                if (!ok) return 0;
                requests.Add(new ActionRequest(path, logger, null, fileOptions));
            }

            return RunQueuedRequestsWithProgressForm(action, requests, logger, actionId);
        }
    }

    private static void SendPathsToCompressPrimary(string pipeName, IEnumerable<string> paths, AppLogger logger)
    {
        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);
        client.Connect(5000);
        using var writer = new StreamWriter(client, Encoding.UTF8) { AutoFlush = true };
        foreach (var path in paths)
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                writer.WriteLine(path);
                logger.Log($"RunCompressBatch: sent path to primary: {path}");
            }
        }
    }
}
