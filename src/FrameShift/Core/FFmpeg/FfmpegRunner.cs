using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FrameShift.Core.Actions;
using FrameShift.Core.Logging;
using FrameShift.Core.Progress;

namespace FrameShift.Core.FFmpeg;

public sealed record FfmpegRunResult(
    int ExitCode,
    string StandardError,
    bool Canceled,
    CancellationScope CancellationScope,
    bool ProcessTerminationConfirmed = true);

internal sealed class FfmpegProgressState
{
    private readonly object _syncRoot = new();
    private readonly long _startedTimestamp = Stopwatch.GetTimestamp();
    private int _displayedPermille;
    private int _realPermille;
    private bool _receivedRealProgress;
    private bool _finalizing;
    private bool _finished;
    private double? _estimatedTotalWallSeconds;

    public bool Finished
    {
        get
        {
            lock (_syncRoot)
            {
                return _finished;
            }
        }
    }

    public void UpdateRealProgress(double currentSeconds, TimeSpan duration)
    {
        if (duration.TotalSeconds <= 0)
        {
            return;
        }

        lock (_syncRoot)
        {
            var rawPermille = (int)Math.Clamp(
                Math.Floor((currentSeconds / duration.TotalSeconds) * 1000d),
                0d,
                999d);

            UpdateProgressRatio(rawPermille, currentSeconds / duration.TotalSeconds);
        }
    }

    public void UpdateFrameProgress(long currentFrame, long totalFrames)
    {
        if (currentFrame <= 0 || totalFrames <= 0)
        {
            return;
        }

        lock (_syncRoot)
        {
            var completionRatio = currentFrame / (double)totalFrames;
            var rawPermille = (int)Math.Clamp(
                Math.Floor(completionRatio * 1000d),
                0d,
                999d);

            UpdateProgressRatio(rawPermille, completionRatio);
        }
    }

    private void UpdateProgressRatio(int rawPermille, double completionRatio)
    {
        if (rawPermille > _realPermille)
        {
            _realPermille = rawPermille;
        }

        _receivedRealProgress = true;

        if (completionRatio > 0d && _realPermille > 0)
        {
            var elapsedWallSeconds = GetElapsedWallSeconds();
            if (elapsedWallSeconds > 0.1d)
            {
                var sampledTotalWallSeconds = elapsedWallSeconds / completionRatio;
                if (sampledTotalWallSeconds > 0.1d)
                {
                    _estimatedTotalWallSeconds = _estimatedTotalWallSeconds is null
                        ? sampledTotalWallSeconds
                        : (_estimatedTotalWallSeconds.Value * 0.70d) + (sampledTotalWallSeconds * 0.30d);
                }
            }
        }
    }

    public void MarkFinalizing()
    {
        lock (_syncRoot)
        {
            _finalizing = true;
        }
    }

    public void MarkFinished()
    {
        lock (_syncRoot)
        {
            _finalizing = true;
            _finished = true;
        }
    }

    public FfmpegProgressSnapshot AdvanceSnapshot()
    {
        lock (_syncRoot)
        {
            var targetPermille = _realPermille;

            if (!_receivedRealProgress)
            {
                var preparingPermille = (int)Math.Clamp(
                    Math.Floor(GetElapsedWallSeconds() * 8d),
                    0d,
                    25d);
                targetPermille = Math.Max(targetPermille, preparingPermille);
            }
            else if (!_finalizing && _estimatedTotalWallSeconds is double estimatedTotalWallSeconds && estimatedTotalWallSeconds > 0.1d)
            {
                var estimatedPermille = (int)Math.Clamp(
                    Math.Floor((GetElapsedWallSeconds() / estimatedTotalWallSeconds) * 1000d),
                    0d,
                    985d);

                if (estimatedPermille > targetPermille)
                {
                    targetPermille = estimatedPermille;
                }
            }

            if (_finalizing)
            {
                targetPermille = Math.Max(targetPermille, _finished ? 1000 : 990);
            }

            if (targetPermille > _displayedPermille)
            {
                var delta = targetPermille - _displayedPermille;
                var step = (int)Math.Ceiling(delta * 0.18d);
                if (step < 1)
                {
                    step = 1;
                }

                if (step > 35)
                {
                    step = 35;
                }

                _displayedPermille = Math.Min(targetPermille, _displayedPermille + step);
            }

            if (_finished && _displayedPermille < 1000)
            {
                _displayedPermille = 1000;
            }

            var phase = _finished || _finalizing
                ? FfmpegProgressPhase.Finalizing
                : _receivedRealProgress
                    ? FfmpegProgressPhase.Processing
                    : FfmpegProgressPhase.Preparing;

            var remainingSeconds = default(double?);
            if (_receivedRealProgress &&
                !_finalizing &&
                _estimatedTotalWallSeconds is double currentEstimatedTotalWallSeconds)
            {
                var candidate = currentEstimatedTotalWallSeconds - GetElapsedWallSeconds();
                if (candidate > 1d)
                {
                    remainingSeconds = candidate;
                }
            }

            return new FfmpegProgressSnapshot(_displayedPermille, phase, remainingSeconds);
        }
    }

    private double GetElapsedWallSeconds()
    {
        var elapsedTicks = Stopwatch.GetTimestamp() - _startedTimestamp;
        return elapsedTicks / (double)Stopwatch.Frequency;
    }
}

internal enum FfmpegProgressPhase
{
    Preparing,
    Processing,
    Finalizing
}

internal readonly record struct FfmpegProgressSnapshot(int DisplayedPermille, FfmpegProgressPhase Phase, double? RemainingSeconds);

public sealed class FfmpegRunner
{
    private readonly AppLogger _logger;
    private readonly ConcurrentDictionary<(string ExecutablePath, string EncoderName), bool> _encoderCache = new();

    public FfmpegRunner(AppLogger logger)
    {
        _logger = logger;
    }

    public ProcessStartInfo CreateStartInfo(string executablePath, IReadOnlyCollection<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    internal RawVideoProcess StartRawVideoProcess(
        string executablePath,
        IReadOnlyCollection<string> arguments,
        bool redirectStandardInput,
        bool drainStandardOutput,
        string role,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var process = new Process { StartInfo = CreateStartInfo(executablePath, arguments) };
        process.StartInfo.RedirectStandardInput = redirectStandardInput;

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException($"FFmpeg {role} process did not start.");
            }

            return new RawVideoProcess(this, process, drainStandardOutput, role, cancellationToken);
        }
        catch
        {
            process.Dispose();
            throw;
        }
    }

    public async Task<FfmpegRunResult> RunAsync(
        string executablePath,
        IReadOnlyCollection<string> arguments,
        TimeSpan? duration,
        IProgressReporter? progressReporter,
        string? currentFile,
        string? currentAction,
        string? executionMode,
        CancellationToken cancellationToken)
    {
        return await RunAsync(
            executablePath,
            arguments,
            duration,
            null,
            progressReporter,
            currentFile,
            currentAction,
            executionMode,
            null,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<FfmpegRunResult> RunAsync(
        string executablePath,
        IReadOnlyCollection<string> arguments,
        TimeSpan? duration,
        long? estimatedFrameCount,
        IProgressReporter? progressReporter,
        string? currentFile,
        string? currentAction,
        string? executionMode,
        CancellationToken cancellationToken)
    {
        return await RunAsync(
            executablePath,
            arguments,
            duration,
            estimatedFrameCount,
            progressReporter,
            currentFile,
            currentAction,
            executionMode,
            null,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<FfmpegRunResult> RunAsync(
        string executablePath,
        IReadOnlyCollection<string> arguments,
        TimeSpan? duration,
        long? estimatedFrameCount,
        IProgressReporter? progressReporter,
        string? currentFile,
        string? currentAction,
        string? executionMode,
        string? pipelineDescription,
        CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = CreateStartInfo(executablePath, arguments) };
        var stderrBuilder = new StringBuilder();
        var canceled = false;
        var cancellationScope = CancellationScope.None;
        var progressState = new FfmpegProgressState();
        using var itemCancellationSource = new CancellationTokenSource();
        using var monitorStopSource = new CancellationTokenSource();
        using var streamReadCancellationSource = new CancellationTokenSource();

        _logger.Log($"FFmpeg command: {Path.GetFileName(executablePath)} {FormatArguments(arguments)}");
        process.Start();
        _logger.Log($"FfmpegRunner: process started. pid={process.Id}, currentFile={currentFile ?? "<none>"}, currentAction={currentAction ?? "<none>"}.");

        using var globalRegistration = cancellationToken.Register(() =>
        {
            _logger.Log($"FfmpegRunner: cancellation requested (global). pid={TryGetProcessId(process)}, currentFile={currentFile ?? "<none>"}.");
            canceled = true;
            SetCancellationScope(ref cancellationScope, CancellationScope.All);
            progressState.MarkFinalizing();
            TryKill(process, "global", currentFile);
        });

        using var itemRegistration = itemCancellationSource.Token.Register(() =>
        {
            _logger.Log($"FfmpegRunner: cancellation requested (current-item). pid={TryGetProcessId(process)}, currentFile={currentFile ?? "<none>"}.");
            canceled = true;
            SetCancellationScope(ref cancellationScope, CancellationScope.CurrentItem);
            progressState.MarkFinalizing();
            TryKill(process, "current-item", currentFile);
        });

        var visualProgressTask = ReportVisualProgressAsync(
            progressReporter,
            currentFile,
            currentAction,
            executionMode,
            pipelineDescription,
            progressState,
            monitorStopSource.Token);
        var stdoutTask = ReadProgressAsync(
            process.StandardOutput,
            duration,
            estimatedFrameCount,
            progressState,
            streamReadCancellationSource.Token);
        var stderrTask = ReadErrorAsync(process.StandardError, stderrBuilder, streamReadCancellationSource.Token);
        var queueCancellationTask = MonitorCurrentQueueItemCancellationAsync(
            progressReporter,
            currentFile,
            process,
            itemCancellationSource,
            progressState,
            monitorStopSource.Token);

        int exitCode;
        var processTerminationConfirmed = true;
        if (canceled)
        {
            processTerminationConfirmed = await WaitForExitAfterCancellationAsync(process).ConfigureAwait(false);
            exitCode = processTerminationConfirmed ? process.ExitCode : -1;
        }
        else
        {
            _logger.Log($"FfmpegRunner: WaitForExitAsync started (normal). pid={TryGetProcessId(process)}.");
            await process.WaitForExitAsync().ConfigureAwait(false);
            exitCode = process.ExitCode;
            _logger.Log($"FfmpegRunner: WaitForExitAsync finished (normal). pid={TryGetProcessId(process)}, exitCode={exitCode}.");
        }

        progressState.MarkFinished();
        monitorStopSource.Cancel();
        await DrainProcessOutputAsync(
            stdoutTask,
            stderrTask,
            streamReadCancellationSource,
            canceled).ConfigureAwait(false);
        await Task.WhenAll(
            SuppressCancellationAsync(visualProgressTask),
            SuppressCancellationAsync(queueCancellationTask)).ConfigureAwait(false);

        _logger.Log($"FfmpegRunner: RunAsync returning. canceled={canceled}, exitCode={exitCode}, scope={cancellationScope}, currentFile={currentFile ?? "<none>"}.");
        return new FfmpegRunResult(exitCode, stderrBuilder.ToString(), canceled, cancellationScope, processTerminationConfirmed);
    }

    public async Task<bool> IsEncoderAvailableAsync(
        string executablePath,
        string encoderName,
        CancellationToken cancellationToken)
    {
        var key = (executablePath, encoderName);
        if (_encoderCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var result = await RunCaptureAsync(
            executablePath,
            new[] { "-hide_banner", "-encoders" },
            cancellationToken).ConfigureAwait(false);

        var available = result.Success &&
                        result.StandardOutput.Contains(encoderName, StringComparison.OrdinalIgnoreCase);
        _encoderCache[key] = available;
        return available;
    }

    public async Task<CaptureResult> RunCaptureAsync(
        string executablePath,
        IReadOnlyCollection<string> arguments,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var process = new Process { StartInfo = CreateStartInfo(executablePath, arguments) };
        process.Start();

        var cancellationSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellationRegistration = cancellationToken.Register(() =>
        {
            _logger.Log($"FfmpegRunner: capture cancellation requested. pid={TryGetProcessId(process)}.");
            TryKill(process, "capture-cancellation", null);
            cancellationSignal.TrySetResult(true);
        });

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        var waitForExitTask = process.WaitForExitAsync();

        if (await Task.WhenAny(waitForExitTask, cancellationSignal.Task).ConfigureAwait(false) != waitForExitTask)
        {
            var processExited = await WaitForExitAfterCancellationAsync(process, waitForExitTask).ConfigureAwait(false);
            if (!processExited)
            {
                throw new TimeoutException("FFmpeg capture process did not exit after cancellation.");
            }
        }
        else
        {
            await waitForExitTask.ConfigureAwait(false);
        }

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        if (!string.IsNullOrWhiteSpace(stderr))
        {
            _logger.Log(stderr.Trim());
        }

        return new CaptureResult(process.ExitCode == 0, stdout, stderr);
    }

    private static async Task ReportVisualProgressAsync(
        IProgressReporter? progressReporter,
        string? currentFile,
        string? currentAction,
        string? executionMode,
        string? pipelineDescription,
        FfmpegProgressState progressState,
        CancellationToken cancellationToken)
    {
        if (progressReporter is null)
        {
            return;
        }

        var lastPermille = -1;
        var lastPhase = (FfmpegProgressPhase?)null;

        while (!cancellationToken.IsCancellationRequested)
        {
            var snapshot = progressState.AdvanceSnapshot();
            if (snapshot.DisplayedPermille != lastPermille || snapshot.Phase != lastPhase)
            {
                progressReporter.ReportProgress(
                    snapshot.DisplayedPermille,
                    currentFile,
                    currentAction,
                    BuildProgressMessage(executionMode, pipelineDescription, snapshot),
                    BuildEtaText(snapshot));

                lastPermille = snapshot.DisplayedPermille;
                lastPhase = snapshot.Phase;
            }

            if (progressState.Finished && snapshot.DisplayedPermille >= 1000)
            {
                return;
            }

            await Task.Delay(120, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task MonitorCurrentQueueItemCancellationAsync(
        IProgressReporter? progressReporter,
        string? currentFile,
        Process process,
        CancellationTokenSource itemCancellationSource,
        FfmpegProgressState progressState,
        CancellationToken cancellationToken)
    {
        if (progressReporter is null || string.IsNullOrWhiteSpace(currentFile))
        {
            return;
        }

        AppLogger.LogStatic($"FfmpegRunner: queue cancellation monitor started. pid={TryGetProcessId(process)}, currentFile={currentFile}.");
        while (!cancellationToken.IsCancellationRequested && !itemCancellationSource.IsCancellationRequested && !process.HasExited)
        {
            if (progressReporter.IsQueueItemCancellationRequested(currentFile))
            {
                AppLogger.LogStatic($"FfmpegRunner: queue cancellation monitor detected current-item cancel. pid={TryGetProcessId(process)}, currentFile={currentFile}.");
                progressState.MarkFinalizing();
                progressReporter.ReportQueueItem(currentFile, "canceling", "Canceling current file...");
                itemCancellationSource.Cancel();
                return;
            }

            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }

        AppLogger.LogStatic($"FfmpegRunner: queue cancellation monitor finished. pid={TryGetProcessId(process)}, currentFile={currentFile}, processHasExited={process.HasExited}, itemCancellationRequested={itemCancellationSource.IsCancellationRequested}, monitorCancellationRequested={cancellationToken.IsCancellationRequested}.");
    }

    private async Task ReadProgressAsync(
        StreamReader reader,
        TimeSpan? duration,
        long? estimatedFrameCount,
        FfmpegProgressState progressState,
        CancellationToken cancellationToken)
    {
        _logger.Log("FfmpegRunner: stdout reader started.");
        try
        {
            while (true)
            {
                var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null)
                {
                    _logger.Log("FfmpegRunner: stdout reader finished.");
                    return;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                if (line.StartsWith("frame=", StringComparison.OrdinalIgnoreCase))
                {
                    if (estimatedFrameCount is null || estimatedFrameCount.Value <= 0)
                    {
                        continue;
                    }

                    if (long.TryParse(line["frame=".Length..].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var frame))
                    {
                        progressState.UpdateFrameProgress(frame, estimatedFrameCount.Value);
                    }
                }
                else if (line.StartsWith("out_time_ms=", StringComparison.OrdinalIgnoreCase))
                {
                    if (!long.TryParse(line["out_time_ms=".Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var outTimeMs))
                    {
                        continue;
                    }

                    if (duration is null || duration.Value.TotalMilliseconds <= 0)
                    {
                        continue;
                    }

                    var currentSeconds = outTimeMs / 1_000_000d;
                    progressState.UpdateRealProgress(currentSeconds, duration.Value);
                }
                else if (line.StartsWith("out_time=", StringComparison.OrdinalIgnoreCase))
                {
                    var outTime = line["out_time=".Length..];
                    if (!TryParseFfmpegTime(outTime, out var outTimeSeconds) || duration is null || duration.Value.TotalSeconds <= 0)
                    {
                        continue;
                    }

                    progressState.UpdateRealProgress(outTimeSeconds, duration.Value);
                }
                else if (line.Equals("progress=end", StringComparison.OrdinalIgnoreCase))
                {
                    progressState.MarkFinalizing();
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.Log("FfmpegRunner: stdout reader canceled.");
            throw;
        }
        catch (Exception ex)
        {
            _logger.Log($"FfmpegRunner: stdout reader failed: {ex}");
            throw;
        }
    }

    private async Task ReadErrorAsync(
        StreamReader reader,
        StringBuilder stderrBuilder,
        CancellationToken cancellationToken)
    {
        _logger.Log("FfmpegRunner: stderr reader started.");
        try
        {
            while (true)
            {
                var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null)
                {
                    _logger.Log("FfmpegRunner: stderr reader finished.");
                    return;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                stderrBuilder.AppendLine(line);
                _logger.Log(line);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.Log("FfmpegRunner: stderr reader canceled.");
            throw;
        }
        catch (Exception ex)
        {
            _logger.Log($"FfmpegRunner: stderr reader failed: {ex}");
            throw;
        }
    }

    internal static bool TryParseFfmpegTime(string value, out double seconds)
    {
        seconds = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Trim().Split(':');
        if (parts.Length is < 2 or > 3)
        {
            return false;
        }

        if (!double.TryParse(parts[^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var secondPart))
        {
            return false;
        }

        if (!double.TryParse(parts[^2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutePart))
        {
            return false;
        }

        var hourPart = 0d;
        if (parts.Length == 3 && !double.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out hourPart))
        {
            return false;
        }

        seconds = (hourPart * 3600d) + (minutePart * 60d) + secondPart;
        return true;
    }

    private void TryKill(Process process, string reason, string? currentFile)
    {
        try
        {
            _logger.Log($"FfmpegRunner: kill process requested. pid={TryGetProcessId(process)}, reason={reason}, currentFile={currentFile ?? "<none>"}, hasExited={process.HasExited}.");
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
            _logger.Log($"FfmpegRunner: kill process returned. pid={TryGetProcessId(process)}, reason={reason}, hasExited={process.HasExited}.");
        }
        catch (Exception ex)
        {
            _logger.Log($"FfmpegRunner: kill process failed. pid={TryGetProcessId(process)}, reason={reason}, error={ex}.");
        }
    }

    private static string BuildProgressMessage(string? executionMode, string? pipelineDescription, FfmpegProgressSnapshot snapshot)
    {
        var baseMessage = snapshot.Phase switch
        {
            FfmpegProgressPhase.Preparing => $"Preparing on {NormalizeExecutionMode(executionMode)}...",
            FfmpegProgressPhase.Finalizing => $"Finalizing on {NormalizeExecutionMode(executionMode)}...",
            _ => $"Processing on {NormalizeExecutionMode(executionMode)}: {Math.Floor(snapshot.DisplayedPermille / 10d):0}%"
        };
        return baseMessage;
    }

    private static string NormalizeExecutionMode(string? executionMode)
    {
        if (string.Equals(executionMode, "GPU", StringComparison.OrdinalIgnoreCase))
        {
            return "GPU";
        }

        return "CPU";
    }

    private static string FormatArguments(IReadOnlyCollection<string> arguments)
    {
        return string.Join(" ", arguments.Select(QuoteArgument));
    }

    private static string QuoteArgument(string argument)
    {
        if (string.IsNullOrEmpty(argument))
        {
            return "\"\"";
        }

        return argument.Any(char.IsWhiteSpace)
            ? $"\"{argument.Replace("\"", "\\\"", StringComparison.Ordinal)}\""
            : argument;
    }

    private static string? BuildEtaText(FfmpegProgressSnapshot snapshot)
    {
        if (snapshot.Phase != FfmpegProgressPhase.Processing || snapshot.RemainingSeconds is null)
        {
            return null;
        }

        return $"Estimated remaining: {FormatDuration(snapshot.RemainingSeconds.Value)}";
    }

    private static string FormatDuration(double seconds)
    {
        if (seconds < 0)
        {
            seconds = 0;
        }

        var rounded = TimeSpan.FromSeconds(Math.Round(seconds));
        if (rounded.TotalHours >= 1)
        {
            return $"{(int)rounded.TotalHours:00}:{rounded.Minutes:00}:{rounded.Seconds:00}";
        }

        return $"{rounded.Minutes:00}:{rounded.Seconds:00}";
    }

    private static async Task SuppressCancellationAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task<bool> WaitForExitAfterCancellationAsync(Process process, Task? pendingWaitForExitTask = null)
    {
        _logger.Log($"FfmpegRunner: WaitForExitAsync started after cancellation. pid={TryGetProcessId(process)}.");
        var waitForExitTask = pendingWaitForExitTask ?? process.WaitForExitAsync();
        var completedTask = await Task.WhenAny(waitForExitTask, Task.Delay(5000)).ConfigureAwait(false);
        if (completedTask == waitForExitTask)
        {
            await waitForExitTask.ConfigureAwait(false);
            _logger.Log($"FfmpegRunner: WaitForExitAsync finished after cancellation. pid={TryGetProcessId(process)}, exitCode={process.ExitCode}.");
            return true;
        }

        _logger.Log($"FfmpegRunner: WaitForExitAsync timeout after cancellation — forcing kill. pid={TryGetProcessId(process)}.");
        TryKill(process, "post-cancellation-timeout", null);

        _logger.Log($"FfmpegRunner: WaitForExitAsync started after second kill. pid={TryGetProcessId(process)}.");
        completedTask = await Task.WhenAny(waitForExitTask, Task.Delay(5000)).ConfigureAwait(false);
        if (completedTask == waitForExitTask)
        {
            await waitForExitTask.ConfigureAwait(false);
            _logger.Log($"FfmpegRunner: WaitForExitAsync finished after second kill. pid={TryGetProcessId(process)}, exitCode={process.ExitCode}.");
            return true;
        }

        _logger.Log($"FfmpegRunner: WaitForExitAsync did not finish after second kill. pid={TryGetProcessId(process)}.");
        return false;
    }

    private async Task DrainProcessOutputAsync(
        Task stdoutTask,
        Task stderrTask,
        CancellationTokenSource streamReadCancellationSource,
        bool canceled)
    {
        var drainTimeoutMilliseconds = canceled ? 1500 : 5000;
        var drainTask = Task.WhenAll(
            SuppressCancellationAsync(stdoutTask),
            SuppressCancellationAsync(stderrTask));

        var completedTask = await Task.WhenAny(drainTask, Task.Delay(drainTimeoutMilliseconds)).ConfigureAwait(false);
        if (completedTask == drainTask)
        {
            await drainTask.ConfigureAwait(false);
            _logger.Log($"FfmpegRunner: DrainProcessOutputAsync finished normally. canceled={canceled}.");
            return;
        }

        _logger.Log("FFmpeg output stream drain timed out. Canceling remaining stream reads.");
        streamReadCancellationSource.Cancel();
        await drainTask.ConfigureAwait(false);
        _logger.Log($"FfmpegRunner: DrainProcessOutputAsync finished after canceling stream reads. canceled={canceled}.");
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

    private static void SetCancellationScope(ref CancellationScope currentScope, CancellationScope requestedScope)
    {
        if (requestedScope > currentScope)
        {
            currentScope = requestedScope;
        }
    }

    internal sealed class RawVideoProcess : IAsyncDisposable
    {
        private readonly FfmpegRunner _owner;
        private readonly Process _process;
        private readonly string _role;
        private readonly CancellationTokenSource _streamReadCancellationSource = new();
        private readonly CancellationTokenRegistration _cancellationRegistration;
        private readonly Task _stderrDrainTask;
        private readonly Task _stdoutDrainTask;
        private readonly StringBuilder _stderr = new();
        private readonly object _drainSync = new();
        private Task? _drainTask;
        private int _standardInputClosed;
        private int _disposed;

        internal RawVideoProcess(
            FfmpegRunner owner,
            Process process,
            bool drainStandardOutput,
            string role,
            CancellationToken cancellationToken)
        {
            _owner = owner;
            _process = process;
            _role = role;
            _cancellationRegistration = cancellationToken.Register(() => RequestStop("rawvideo-cancellation"));
            _stderrDrainTask = DrainStandardErrorAsync();
            _stdoutDrainTask = drainStandardOutput
                ? DrainStandardOutputAsync()
                : Task.CompletedTask;

            _owner._logger.Log($"FfmpegRunner: rawvideo {role} started. pid={TryGetProcessId(process)}.");
        }

        public Stream StandardOutput => _process.StandardOutput.BaseStream;

        public Stream StandardInput => _process.StandardInput.BaseStream;

        public string StandardError => _stderr.ToString();

        public int ExitCode => _process.ExitCode;

        public void RequestStop(string reason)
        {
            CloseStandardInput();
            _owner.TryKill(_process, $"rawvideo-{_role}-{reason}", null);
        }

        public async Task CompleteStandardInputAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
            CloseStandardInput();
        }

        public async Task WaitForExitAsync(CancellationToken cancellationToken)
        {
            try
            {
                await _process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                await StopAndDrainAsync("rawvideo-wait-canceled").ConfigureAwait(false);
                throw;
            }

            await DrainStreamsAsync(canceled: false).ConfigureAwait(false);
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            try
            {
                await StopAndDrainAsync("rawvideo-dispose").ConfigureAwait(false);
            }
            finally
            {
                _cancellationRegistration.Dispose();
                _streamReadCancellationSource.Dispose();
                _process.Dispose();
            }
        }

        private void CloseStandardInput()
        {
            if (Interlocked.Exchange(ref _standardInputClosed, 1) != 0 || !_process.StartInfo.RedirectStandardInput)
            {
                return;
            }

            try
            {
                _process.StandardInput.Close();
            }
            catch (Exception ex)
            {
                _owner._logger.Log($"FfmpegRunner: rawvideo {_role} stdin close failed. pid={TryGetProcessId(_process)}, error={ex.Message}");
            }
        }

        private async Task StopAndDrainAsync(string reason)
        {
            RequestStop(reason);
            var exited = await _owner.WaitForExitAfterCancellationAsync(_process).ConfigureAwait(false);
            await DrainStreamsAsync(canceled: true).ConfigureAwait(false);

            if (!exited)
            {
                _owner._logger.Log($"FfmpegRunner: rawvideo {_role} exit was not confirmed after cancellation. pid={TryGetProcessId(_process)}.");
            }
        }

        private Task DrainStreamsAsync(bool canceled)
        {
            lock (_drainSync)
            {
                _drainTask ??= DrainStreamsCoreAsync(canceled);
                return _drainTask;
            }
        }

        private async Task DrainStreamsCoreAsync(bool canceled)
        {
            var drainTask = Task.WhenAll(
                SuppressStreamReadFailureAsync(_stderrDrainTask, "stderr"),
                SuppressStreamReadFailureAsync(_stdoutDrainTask, "stdout"));
            var timeoutMilliseconds = canceled ? 1500 : 5000;
            if (await Task.WhenAny(drainTask, Task.Delay(timeoutMilliseconds)).ConfigureAwait(false) != drainTask)
            {
                _owner._logger.Log($"FfmpegRunner: rawvideo {_role} stream drain timed out. Canceling internal stream reads.");
                _streamReadCancellationSource.Cancel();
            }

            await drainTask.ConfigureAwait(false);
        }

        private async Task DrainStandardErrorAsync()
        {
            var buffer = new char[4096];
            try
            {
                while (true)
                {
                    var read = await _process.StandardError.ReadAsync(
                        buffer.AsMemory(0, buffer.Length),
                        _streamReadCancellationSource.Token).ConfigureAwait(false);
                    if (read <= 0)
                    {
                        return;
                    }

                    _stderr.Append(buffer, 0, read);
                }
            }
            catch (OperationCanceledException) when (_streamReadCancellationSource.IsCancellationRequested)
            {
            }
        }

        private async Task DrainStandardOutputAsync()
        {
            try
            {
                await _process.StandardOutput.BaseStream.CopyToAsync(
                    Stream.Null,
                    _streamReadCancellationSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_streamReadCancellationSource.IsCancellationRequested)
            {
            }
        }

        private async Task SuppressStreamReadFailureAsync(Task task, string streamName)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _owner._logger.Log($"FfmpegRunner: rawvideo {_role} {streamName} drain failed. pid={TryGetProcessId(_process)}, error={ex.Message}");
            }
        }
    }
}

public sealed record CaptureResult(bool Success, string StandardOutput, string StandardError);
