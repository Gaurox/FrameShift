using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FrameShift.Core.Actions;
using FrameShift.Core.Logging;
using FrameShift.Core.Progress;
using FrameShift.Windows.Batch;
using FrameShift.Windows.ProgressUI;

namespace FrameShift;

internal static partial class Program
{
    // Single authoritative lists — all action-ID dispatch reads from here.
    private static readonly HashSet<string> s_aiBatchActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "remove-background", "separate-audio", "remove-noise", "remove-noise-video", "upscale-image", "upscale-video",
        "create-subtitles-audio", "create-subtitles-video"
    };

    private static readonly HashSet<string> s_conversionBatchActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "convert-video", "convert-audio", "convert-image", "extract-audio", "extract-frames"
    };

    private static bool IsAiBatchAction(string actionId) => s_aiBatchActions.Contains(actionId);

    private static int RunConversionBatch(
        ActionRegistry registry,
        string actionId,
        AppLogger logger,
        IReadOnlyList<string> inputPaths,
        IReadOnlyDictionary<string, string>? options = null)
    {
        logger.Log($"Program: RunConversionBatch entered. actionId={actionId}, inputPathCount={inputPaths.Count}, logPath={AppLogger.LogPath}, baseDirectory={AppContext.BaseDirectory}, processPath={Environment.ProcessPath ?? "<null>"}.");
        var effectiveOptions = options is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(options, StringComparer.OrdinalIgnoreCase);

        var definition = GetConversionBatchDefinition(actionId);
        if (definition is null)
        {
            ShowCliError(MediaActionMessages.UnknownAction(actionId));
            return 1;
        }

        using var batchMutex = new Mutex(true, definition.MutexName, out var isPrimaryInstance);
        var ownsBatchMutex = isPrimaryInstance;
        var preflightCompleted = false;
        try
        {
            if (!isPrimaryInstance)
            {
                // Resolve this launch's real options (model selection, stems, denoise strength, …)
                // in the secondary instance so they travel with the queued item over the pipe,
                // instead of silently inheriting the primary session's options.
                if (!RunBatchActionPreflight(actionId, inputPaths, effectiveOptions, logger))
                {
                    logger.Log($"Program: {definition.ActionId} preflight canceled in secondary instance before queue injection.");
                    return 0;
                }

                preflightCompleted = true;

                try
                {
                    ConversionBatchSession.SendPathsToPrimaryInstance(definition, inputPaths, effectiveOptions);
                    logger.Log($"Program: queued paths into existing {definition.ActionId} session.");
                    return 0;
                }
                catch (Exception ex)
                {
                    logger.Log($"Program: queue injection failed for existing {definition.ActionId} session. {ex}");
                    logger.Log($"Program: waiting for existing {definition.ActionId} session to close so a new UI can be opened.");

                    try
                    {
                        ownsBatchMutex = batchMutex.WaitOne(TimeSpan.FromSeconds(5));
                    }
                    catch (AbandonedMutexException)
                    {
                        ownsBatchMutex = true;
                        logger.Log($"Program: acquired abandoned mutex for {definition.ActionId}; reopening session.");
                    }

                    if (!ownsBatchMutex)
                    {
                        ShowCliError($"FrameShift is already running, but the new {definition.DisplayName.ToLowerInvariant()} file could not be queued.");
                        return 1;
                    }

                    logger.Log($"Program: existing {definition.ActionId} session closed before queue injection; reopening a new session.");
                }
            }

            if (!registry.TryGet(actionId, out var action))
            {
                ShowCliError(MediaActionMessages.UnknownAction(actionId));
                return 1;
            }

            if (!preflightCompleted && !RunBatchActionPreflight(actionId, inputPaths, effectiveOptions, logger))
            {
                logger.Log($"Program: {definition.ActionId} preflight exited before progress window opened.");
                return 0;
            }

            if (actionId.Equals("convert-video", StringComparison.OrdinalIgnoreCase) ||
                actionId.Equals("convert-audio", StringComparison.OrdinalIgnoreCase) ||
                actionId.Equals("convert-image", StringComparison.OrdinalIgnoreCase) ||
                actionId.Equals("extract-audio", StringComparison.OrdinalIgnoreCase))
            {
                return RunDeferredProgressConversionBatch(definition, action, logger, inputPaths);
            }

            using var progressForm = new ProgressForm();
            using var cancellationSource = new CancellationTokenSource();
            progressForm.CancelRequested += (_, _) =>
            {
                logger.Log("Program: RunConversionBatch CancelRequested event received.");
                RequestCancellationAsync(cancellationSource, logger, "batch");
            };

            var session = new ConversionBatchSession(definition, action, logger, progressForm);
            if (effectiveOptions.Count > 0)
            {
                session.SetSharedOptions(effectiveOptions);
            }

            progressForm.Shown += (_, _) => session.Start(inputPaths.ToArray(), cancellationSource.Token);

            logger.Log("Program: before Application.Run (batch progress form).");
            Application.Run(progressForm);
            logger.Log("Program: after Application.Run returns (batch progress form).");
            (action as IDisposable)?.Dispose();
            return session.ExitCode;
        }
        finally
        {
            if (ownsBatchMutex)
            {
                batchMutex.ReleaseMutex();
            }
        }
    }

    // Action-specific preflight (model download/verification and option prompts). Runs in both
    // the primary instance and any secondary instance that queues into an existing session, so
    // the per-launch options reflect the file actually being queued. Returns false when the user
    // canceled or inputs were invalid (the caller exits without opening/queuing).
    private static bool RunBatchActionPreflight(
        string actionId,
        IReadOnlyList<string> inputPaths,
        Dictionary<string, string> effectiveOptions,
        AppLogger logger)
    {
        if (actionId.Equals("remove-background", StringComparison.OrdinalIgnoreCase))
        {
            return EnsureRemoveBackgroundModelReady(logger, effectiveOptions);
        }

        if (actionId.Equals("upscale-image", StringComparison.OrdinalIgnoreCase))
        {
            return EnsureUpscaleOptions(inputPaths, effectiveOptions, logger)
                && EnsureUpscaleModelReady(logger, effectiveOptions, inputPaths: inputPaths);
        }

        if (actionId.Equals("upscale-video", StringComparison.OrdinalIgnoreCase))
        {
            return EnsureUpscaleVideoOptions(inputPaths, effectiveOptions, logger)
                && EnsureUpscaleModelReady(logger, effectiveOptions, "Upscale Video", videoMode: true, inputPaths: inputPaths);
        }

        if (actionId.Equals("create-subtitles-audio", StringComparison.OrdinalIgnoreCase))
        {
            return EnsureCreateSubtitlesOptions(inputPaths, effectiveOptions, logger, videoMode: false)
                && EnsureCreateSubtitlesModelReady(logger, effectiveOptions);
        }

        if (actionId.Equals("create-subtitles-video", StringComparison.OrdinalIgnoreCase))
        {
            return EnsureCreateSubtitlesOptions(inputPaths, effectiveOptions, logger, videoMode: true)
                && EnsureCreateSubtitlesModelReady(logger, effectiveOptions);
        }

        if (actionId.Equals("separate-audio", StringComparison.OrdinalIgnoreCase))
        {
            return EnsureSeparateAudioOptions(inputPaths, effectiveOptions, logger)
                && EnsureSeparateAudioModelReady(logger, effectiveOptions);
        }

        if (actionId.Equals("remove-noise", StringComparison.OrdinalIgnoreCase))
        {
            return ValidateRemoveNoiseInputs(inputPaths)
                && EnsureRemoveNoiseAudioOptions(inputPaths, effectiveOptions, logger)
                && EnsureRemoveNoiseModelReady(logger);
        }

        if (actionId.Equals("remove-noise-video", StringComparison.OrdinalIgnoreCase))
        {
            return ValidateRemoveNoiseVideoInputs(inputPaths)
                && EnsureRemoveNoiseVideoOptions(inputPaths, effectiveOptions, logger)
                && EnsureRemoveNoiseModelReady(logger);
        }

        return true;
    }

    private static int RunDeferredProgressConversionBatch(
        ConversionBatchSession.BatchDefinition definition,
        IFrameShiftAction action,
        AppLogger logger,
        IReadOnlyList<string> inputPaths)
    {
        logger.Log($"Program: RunDeferredProgressConversionBatch entered. actionId={definition.ActionId}, inputPathCount={inputPaths.Count}.");

        using var cancellationSource = new CancellationTokenSource();
        var session = new ConversionBatchSession(definition, action, logger);
        session.Initialize(inputPaths.ToArray(), cancellationSource.Token);

        try
        {
            session.WaitForPickerDebounce(cancellationSource.Token);
            var optionResult = session.PromptForSharedOptions();
            if (!optionResult.Success || optionResult.Options is null)
            {
                logger.Log("Program: RunDeferredProgressConversionBatch exited before showing progress because picker was canceled or failed.");
                return session.ExitCode;
            }

            session.SetSharedOptions(optionResult.Options);
        }
        catch (OperationCanceledException)
        {
            logger.Log("Program: RunDeferredProgressConversionBatch canceled before progress window opened.");
            return session.ExitCode;
        }
        finally
        {
            if (session.ExitCode != 0 && cancellationSource.IsCancellationRequested)
            {
                logger.Log("Program: RunDeferredProgressConversionBatch cancellation detected during picker phase.");
            }
        }

        using var progressForm = new ProgressForm();
        session.AttachProgressForm(progressForm);
        progressForm.CancelRequested += (_, _) =>
        {
            logger.Log("Program: RunDeferredProgressConversionBatch CancelRequested event received.");
            RequestCancellationAsync(cancellationSource, logger, "batch-deferred-progress");
        };

        progressForm.Shown += (_, _) => session.StartProcessing(cancellationSource.Token);

        logger.Log("Program: before Application.Run (deferred batch progress form).");
        Application.Run(progressForm);
        logger.Log("Program: after Application.Run returns (deferred batch progress form).");
        (action as IDisposable)?.Dispose();
        return session.ExitCode;
    }

    private static int RunImmediateAction(
        IFrameShiftAction action,
        string inputPath,
        IReadOnlyDictionary<string, string> options,
        AppLogger logger)
    {
        try
        {
            var result = action.ExecuteAsync(
                new ActionRequest(inputPath, logger, null, options),
                CancellationToken.None).GetAwaiter().GetResult();

            if (!result.Success && !result.Canceled)
            {
                logger.Log($"ACTION FAILED ({action.Descriptor.Id}): {result.Message}");
                ShowCliError(result.Message);
                return 1;
            }

            return 0;
        }
        catch (Exception ex)
        {
            logger.Log(ex.ToString());
            ShowCliError(ConversionActionHelper.GetFriendlyExceptionMessage(ex, MediaActionMessages.UnhandledError("An unexpected error occurred.")));
            return 1;
        }
    }

    private static async Task<RunActionResult> ExecuteQueuedActionAsync(
        IFrameShiftAction action,
        IReadOnlyList<string> inputPaths,
        IReadOnlyDictionary<string, string> options,
        AppLogger logger,
        IProgressReporter progressReporter,
        CancellationToken cancellationToken)
    {
        var queueRunner = new ActionQueueRunner();
        var requests = inputPaths
            .Select(path => new ActionRequest(path, logger, progressReporter, options))
            .ToArray();

        var results = await queueRunner.RunAsync(action, requests, cancellationToken).ConfigureAwait(false);
        var anyFailure = results.Any(result => !result.Success && !result.Canceled);
        var failureMessage = results.FirstOrDefault(result => !result.Success && !result.Canceled)?.Message;
        var anySuccess = results.Any(result => result.Success);
        var allCanceled = results.Any() && results.All(result => result.Canceled);
        var anyGlobalCancellation = results.Any(result => result.CancellationScope == CancellationScope.All);
        var anyCurrentItemCancellation = results.Any(result => result.CancellationScope == CancellationScope.CurrentItem);
        var shouldCloseWindow =
            !anyFailure &&
            (anyGlobalCancellation || anySuccess || !(allCanceled && anyCurrentItemCancellation));

        return new RunActionResult(anyFailure ? 1 : 0, failureMessage, shouldCloseWindow);
    }

    private static int RunQueuedActionWithProgressForm(
        IFrameShiftAction action,
        IReadOnlyList<string> inputPaths,
        IReadOnlyDictionary<string, string> options,
        AppLogger logger,
        string cancellationSourceName)
    {
        using var progressForm = new ProgressForm();
        using var cancellationSource = new CancellationTokenSource();
        progressForm.CancelRequested += (_, _) =>
        {
            logger.Log($"Program: {cancellationSourceName} CancelRequested event received.");
            RequestCancellationAsync(cancellationSource, logger, cancellationSourceName);
        };

        var exitCode = 1;
        string? failureMessage = null;
        var shouldCloseWindow = true;
        progressForm.Shown += async (_, _) =>
        {
            try
            {
                var runResult = await ExecuteQueuedActionAsync(action, inputPaths, options, logger, progressForm, cancellationSource.Token).ConfigureAwait(true);
                exitCode = runResult.ExitCode;
                failureMessage = runResult.FailureMessage;
                shouldCloseWindow = runResult.ShouldCloseWindow;
                if (exitCode != 0 && !string.IsNullOrWhiteSpace(failureMessage))
                {
                    logger.Log($"ACTION FAILED ({action.Descriptor.Id}): {failureMessage}");
                    ShowCliError(failureMessage);
                }
            }
            catch (Exception ex)
            {
                logger.Log(ex.ToString());
                ShowCliError(ConversionActionHelper.GetFriendlyExceptionMessage(ex, MediaActionMessages.UnhandledError("An unexpected error occurred.")));
                exitCode = 1;
            }
            finally
            {
                if (!progressForm.IsDisposed)
                {
                    if (shouldCloseWindow || cancellationSource.IsCancellationRequested)
                    {
                        progressForm.CloseSafely();
                    }
                    else
                    {
                        progressForm.EnableCloseMode(failureMessage);
                    }
                }
            }
        };

        logger.Log($"Program: before Application.Run ({cancellationSourceName} progress form).");
        Application.Run(progressForm);
        logger.Log($"Program: after Application.Run returns ({cancellationSourceName} progress form).");
        (action as IDisposable)?.Dispose();
        return exitCode;
    }

    private static async Task<RunActionResult> ExecuteQueuedRequestsAsync(
        IFrameShiftAction action,
        IReadOnlyList<ActionRequest> requests,
        IProgressReporter progressReporter,
        CancellationToken cancellationToken)
    {
        var queueRunner = new ActionQueueRunner();
        var withReporter = requests
            .Select(r => r with { ProgressReporter = progressReporter })
            .ToArray();

        var results = await queueRunner.RunAsync(action, withReporter, cancellationToken).ConfigureAwait(false);
        var anyFailure = results.Any(result => !result.Success && !result.Canceled);
        var failureMessage = results.FirstOrDefault(result => !result.Success && !result.Canceled)?.Message;
        var anySuccess = results.Any(result => result.Success);
        var allCanceled = results.Any() && results.All(result => result.Canceled);
        var anyGlobalCancellation = results.Any(result => result.CancellationScope == CancellationScope.All);
        var anyCurrentItemCancellation = results.Any(result => result.CancellationScope == CancellationScope.CurrentItem);
        var shouldCloseWindow =
            !anyFailure &&
            (anyGlobalCancellation || anySuccess || !(allCanceled && anyCurrentItemCancellation));

        return new RunActionResult(anyFailure ? 1 : 0, failureMessage, shouldCloseWindow);
    }

    private static int RunQueuedRequestsWithProgressForm(
        IFrameShiftAction action,
        IReadOnlyList<ActionRequest> requests,
        AppLogger logger,
        string cancellationSourceName)
    {
        using var progressForm = new ProgressForm();
        using var cancellationSource = new CancellationTokenSource();
        progressForm.CancelRequested += (_, _) =>
        {
            logger.Log($"Program: {cancellationSourceName} CancelRequested event received.");
            RequestCancellationAsync(cancellationSource, logger, cancellationSourceName);
        };

        var exitCode = 1;
        string? failureMessage = null;
        var shouldCloseWindow = true;
        progressForm.Shown += async (_, _) =>
        {
            try
            {
                var runResult = await ExecuteQueuedRequestsAsync(action, requests, progressForm, cancellationSource.Token).ConfigureAwait(true);
                exitCode = runResult.ExitCode;
                failureMessage = runResult.FailureMessage;
                shouldCloseWindow = runResult.ShouldCloseWindow;
                if (exitCode != 0 && !string.IsNullOrWhiteSpace(failureMessage))
                {
                    logger.Log($"ACTION FAILED ({action.Descriptor.Id}): {failureMessage}");
                    ShowCliError(failureMessage);
                }
            }
            catch (Exception ex)
            {
                logger.Log(ex.ToString());
                ShowCliError(ConversionActionHelper.GetFriendlyExceptionMessage(ex, MediaActionMessages.UnhandledError("An unexpected error occurred.")));
                exitCode = 1;
            }
            finally
            {
                if (!progressForm.IsDisposed)
                {
                    if (shouldCloseWindow || cancellationSource.IsCancellationRequested)
                        progressForm.CloseSafely();
                    else
                        progressForm.EnableCloseMode(failureMessage);
                }
            }
        };

        logger.Log($"Program: before Application.Run ({cancellationSourceName} progress form).");
        Application.Run(progressForm);
        logger.Log($"Program: after Application.Run returns ({cancellationSourceName} progress form).");
        (action as IDisposable)?.Dispose();
        return exitCode;
    }

    internal static bool ShouldRunConversionBatch(string actionId, IReadOnlyDictionary<string, string> options)
        => actionId.Equals("extract-frames", StringComparison.OrdinalIgnoreCase) ||
           (!options.Any() && s_conversionBatchActions.Contains(actionId));

    private static ConversionBatchSession.BatchDefinition? GetConversionBatchDefinition(string actionId)
    {
        if (actionId.Equals("convert-video", StringComparison.OrdinalIgnoreCase))
        {
            return ConversionBatchSession.CreateVideoDefinition();
        }

        if (actionId.Equals("convert-audio", StringComparison.OrdinalIgnoreCase))
        {
            return ConversionBatchSession.CreateAudioDefinition();
        }

        if (actionId.Equals("convert-image", StringComparison.OrdinalIgnoreCase))
        {
            return ConversionBatchSession.CreateImageDefinition();
        }

        if (actionId.Equals("extract-audio", StringComparison.OrdinalIgnoreCase))
        {
            return ConversionBatchSession.CreateExtractAudioDefinition();
        }

        if (actionId.Equals("extract-frames", StringComparison.OrdinalIgnoreCase))
        {
            return ConversionBatchSession.CreateExtractFramesDefinition();
        }

        if (actionId.Equals("remove-background", StringComparison.OrdinalIgnoreCase))
        {
            return ConversionBatchSession.CreateRemoveBackgroundDefinition();
        }

        if (actionId.Equals("separate-audio", StringComparison.OrdinalIgnoreCase))
        {
            return ConversionBatchSession.CreateSeparateAudioDefinition();
        }

        if (actionId.Equals("remove-noise", StringComparison.OrdinalIgnoreCase))
        {
            return ConversionBatchSession.CreateRemoveNoiseDefinition();
        }

        if (actionId.Equals("remove-noise-video", StringComparison.OrdinalIgnoreCase))
        {
            return ConversionBatchSession.CreateRemoveNoiseVideoDefinition();
        }

        if (actionId.Equals("upscale-image", StringComparison.OrdinalIgnoreCase))
        {
            return ConversionBatchSession.CreateUpscaleImageDefinition();
        }

        if (actionId.Equals("upscale-video", StringComparison.OrdinalIgnoreCase))
        {
            return ConversionBatchSession.CreateUpscaleVideoDefinition();
        }

        if (actionId.Equals("create-subtitles-audio", StringComparison.OrdinalIgnoreCase))
        {
            return ConversionBatchSession.CreateCreateSubtitlesAudioDefinition();
        }

        if (actionId.Equals("create-subtitles-video", StringComparison.OrdinalIgnoreCase))
        {
            return ConversionBatchSession.CreateCreateSubtitlesVideoDefinition();
        }

        return null;
    }

    private static void RequestCancellationAsync(CancellationTokenSource cancellationSource, AppLogger logger, string source)
    {
        _ = Task.Run(() =>
        {
            try
            {
                logger.Log($"Program: cancellationSource.Cancel started ({source}).");
                cancellationSource.Cancel();
                logger.Log($"Program: cancellationSource.Cancel finished ({source}).");
            }
            catch (ObjectDisposedException)
            {
                logger.Log($"Program: cancellationSource.Cancel hit disposed source ({source}).");
            }
            catch (Exception ex)
            {
                logger.Log($"Program: cancellationSource.Cancel failed ({source}): {ex}");
            }
        });
    }

    private sealed record RunActionResult(int ExitCode, string? FailureMessage, bool ShouldCloseWindow);
}
