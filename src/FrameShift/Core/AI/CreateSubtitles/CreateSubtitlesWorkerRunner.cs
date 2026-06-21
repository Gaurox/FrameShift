using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FrameShift.Core.Logging;

namespace FrameShift.Core.AI.CreateSubtitles;

internal sealed class CreateSubtitlesWorkerRunner
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly AppLogger _logger;

    public CreateSubtitlesWorkerRunner(AppLogger logger)
    {
        _logger = logger;
    }

    public async Task<CreateSubtitlesWorkerResponse> RunAsync(
        CreateSubtitlesWorkerRequest request,
        Action<int, string>? onProgress,
        CancellationToken cancellationToken)
    {
        var workerPath = ResolveWorkerPath();
        Directory.CreateDirectory(Path.GetDirectoryName(request.ResponsePath)!);
        await File.WriteAllTextAsync(
            request.ResponsePath + ".request.json",
            JsonSerializer.Serialize(request, s_jsonOptions),
            cancellationToken).ConfigureAwait(false);

        var startInfo = new ProcessStartInfo
        {
            FileName = workerPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("--request");
        startInfo.ArgumentList.Add(request.ResponsePath + ".request.json");

        using var process = new Process { StartInfo = startInfo };
        var stderr = new StringBuilder();

        process.Start();
        _logger.Log($"CreateSubtitlesWorkerRunner: started worker pid={process.Id}, path={workerPath}.");

        using var cancellationRegistration = cancellationToken.Register(() =>
        {
            TrySignalCancellation(request.CancelSignalPath);
            _logger.Log($"CreateSubtitlesWorkerRunner: cancellation signal written. pid={TryGetProcessId(process)}.");
        });

        // Drain stdout/stderr without the user token — pipe buffers must not fill up
        // while the process is still running. Tasks self-terminate on EOF when the
        // process exits or is killed.
        var stdoutTask = Task.Run(async () =>
        {
            while (true)
            {
                var line = await process.StandardOutput.ReadLineAsync(CancellationToken.None).ConfigureAwait(false);
                if (line is null) return;
                if (string.IsNullOrWhiteSpace(line)) continue;
                TryHandleProgressLine(line, onProgress);
            }
        });

        var stderrTask = Task.Run(async () =>
        {
            while (true)
            {
                var line = await process.StandardError.ReadLineAsync(CancellationToken.None).ConfigureAwait(false);
                if (line is null) return;
                if (string.IsNullOrWhiteSpace(line)) continue;
                stderr.AppendLine(line);
                _logger.Log($"CreateSubtitlesWorkerRunner: {line}");
            }
        });

        // 6-hour hang guard prevents infinite wait if the worker deadlocks.
        // Not a transcription time budget — even hours of audio on CPU finish within this window.
        using var hangGuard = new CancellationTokenSource(TimeSpan.FromHours(6));
        using var waitSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, hangGuard.Token);

        bool hangKill = false;
        try
        {
            await process.WaitForExitAsync(waitSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (hangGuard.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            _logger.Log($"CreateSubtitlesWorkerRunner: worker exceeded 6-hour hang guard, killing. pid={TryGetProcessId(process)}.");
            await WaitForExitOrKillAsync(process).ConfigureAwait(false);
            hangKill = true;
        }
        catch (OperationCanceledException)
        {
            // User cancellation — cancel signal already written by the registration above
            await WaitForExitOrKillAsync(process).ConfigureAwait(false);
        }

        await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);

        if (hangKill)
        {
            throw new TimeoutException("The subtitle worker exceeded the maximum allowed duration and was terminated.");
        }

        var responsePath = request.ResponsePath;
        if (!File.Exists(responsePath))
        {
            var stderrText = stderr.ToString().Trim();
            _logger.Log($"CreateSubtitlesWorkerRunner: no response file. exitCode={process.ExitCode}, stderr={stderrText}");
            var errorMessage = string.IsNullOrEmpty(stderrText)
                ? $"Whisper worker crashed (exit code {process.ExitCode}). The model files may be missing or corrupted."
                : $"Whisper worker exited with code {process.ExitCode}: {stderrText}";
            throw new InvalidOperationException(errorMessage);
        }

        await using var responseStream = File.OpenRead(responsePath);
        CreateSubtitlesWorkerResponse? response;
        try
        {
            response = await JsonSerializer.DeserializeAsync<CreateSubtitlesWorkerResponse>(
                responseStream,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException jsonEx)
        {
            _logger.Log($"CreateSubtitlesWorkerRunner: response JSON parse failed: {jsonEx.Message}");
            throw new InvalidOperationException($"Whisper worker returned an unreadable response (exit code {process.ExitCode}).", jsonEx);
        }

        if (response is null)
        {
            throw new InvalidOperationException("Subtitle worker returned an empty response.");
        }

        if (!response.Success && !response.Canceled && process.ExitCode != 0 && string.IsNullOrWhiteSpace(response.ErrorMessage))
        {
            response.ErrorMessage = stderr.ToString().Trim();
        }

        _logger.Log($"CreateSubtitlesWorkerRunner: worker exited. pid={process.Id}, exitCode={process.ExitCode}, success={response.Success}, canceled={response.Canceled}.");
        return response;
    }

    private static string ResolveWorkerPath()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Workers", "CreateSubtitlesWorker", "FrameShift.SubtitlesWorker.exe"),
            Path.Combine(repositoryRoot, "src", "FrameShift.SubtitlesWorker", "bin", "Debug", "net8.0-windows", "win-x64", "FrameShift.SubtitlesWorker.exe"),
            Path.Combine(repositoryRoot, "src", "FrameShift.SubtitlesWorker", "bin", "Release", "net8.0-windows", "win-x64", "FrameShift.SubtitlesWorker.exe")
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException("Create Subtitles worker was not found.", candidates[0]);
    }

    private static void TryHandleProgressLine(string line, Action<int, string>? onProgress)
    {
        if (onProgress is null)
        {
            return;
        }

        try
        {
            var progress = JsonSerializer.Deserialize<CreateSubtitlesWorkerProgressEvent>(line);
            if (progress is null || !string.Equals(progress.Type, "progress", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            onProgress(Math.Clamp(progress.Percent, 0, 100), progress.Message);
        }
        catch (JsonException)
        {
        }
    }

    private static void TrySignalCancellation(string cancelSignalPath)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cancelSignalPath)!);
            if (!File.Exists(cancelSignalPath))
            {
                File.WriteAllText(cancelSignalPath, "cancel");
            }
        }
        catch
        {
        }
    }

    private static async Task WaitForExitOrKillAsync(Process process)
    {
        var exitedTask = process.WaitForExitAsync();
        var completedTask = await Task.WhenAny(exitedTask, Task.Delay(TimeSpan.FromSeconds(20))).ConfigureAwait(false);
        if (completedTask == exitedTask)
        {
            await exitedTask.ConfigureAwait(false);
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }
    }

    private static int TryGetProcessId(Process process)
    {
        try
        {
            return process.Id;
        }
        catch
        {
            return -1;
        }
    }
}
