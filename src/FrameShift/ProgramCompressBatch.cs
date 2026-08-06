using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using FrameShift.Core.Actions;
using FrameShift.Core.Logging;
using FrameShift.Windows.Batch;
using FrameShift.Windows.Forms;
using FrameShift.Windows.ProgressUI;

namespace FrameShift;

internal static partial class Program
{
    internal static bool IsHeadlessCompressionInvocation(
        string actionId,
        IReadOnlyDictionary<string, string> options) => actionId switch
        {
            "compress-video" or "compress-audio" => options.ContainsKey(ActionOptionKeys.Profile),
            "compress-image" => options.ContainsKey(ActionOptionKeys.Profile) ||
                                options.ContainsKey(ActionOptionKeys.Target) ||
                                options.ContainsKey(ActionOptionKeys.TargetSizeBytes),
            _ => false
        };

    private static int RunCompressBatch(string actionId, IFrameShiftAction action, List<string> inputPaths, AppLogger logger)
    {
        var definition = GetCompressionBatchDefinition(actionId);
        if (definition is null)
        {
            logger.Log($"RunCompressBatch: unknown actionId={actionId}");
            return 1;
        }

        using var batchMutex = new Mutex(true, definition.MutexName, out var isPrimaryInstance);
        var ownsBatchMutex = isPrimaryInstance;
        try
        {
            if (!isPrimaryInstance)
            {
                try
                {
                    if (ConversionBatchSession.SendPathsToPrimaryInstance(definition, inputPaths))
                    {
                        logger.Log($"RunCompressBatch: queued {inputPaths.Count} path(s) into existing {actionId} session.");
                        return 0;
                    }

                    logger.Log($"RunCompressBatch: existing {actionId} session rejected the queued invocation.");
                }
                catch (Exception ex)
                {
                    logger.Log($"RunCompressBatch: queue injection failed for {actionId}: {ex}");
                }

                try
                {
                    ownsBatchMutex = batchMutex.WaitOne(TimeSpan.FromSeconds(5));
                }
                catch (AbandonedMutexException)
                {
                    ownsBatchMutex = true;
                    logger.Log($"RunCompressBatch: acquired abandoned mutex for {actionId}; reopening session.");
                }

                if (!ownsBatchMutex)
                {
                    ShowCliError("FrameShift is already running, but the file could not be queued.");
                    return 1;
                }

                logger.Log($"RunCompressBatch: existing {actionId} session closed before accepting the invocation; opening a new session.");
            }

            using var cancellationSource = new CancellationTokenSource();
            var session = new ConversionBatchSession(definition, action, logger);
            session.Initialize(inputPaths.ToArray(), cancellationSource.Token);

            try
            {
                session.WaitForPickerDebounce(cancellationSource.Token);
                if (!ConfigureInitialCompressionBatch(actionId, session, logger))
                {
                    logger.Log($"RunCompressBatch: compression configuration ended before progress window opened. actionId={actionId}.");
                    return 0;
                }

                using var progressForm = new ProgressForm();
                session.AttachProgressForm(progressForm);
                progressForm.CancelRequested += (_, _) =>
                {
                    logger.Log($"Program: {actionId} CancelRequested event received.");
                    RequestCancellationAsync(cancellationSource, logger, actionId);
                };
                progressForm.Shown += (_, _) => session.StartProcessing(cancellationSource.Token);

                logger.Log($"Program: before Application.Run ({actionId} progress form).");
                Application.Run(progressForm);
                logger.Log($"Program: after Application.Run returns ({actionId} progress form).");
                return session.ExitCode;
            }
            finally
            {
                session.Close();
                (action as IDisposable)?.Dispose();
            }
        }
        finally
        {
            if (ownsBatchMutex)
            {
                batchMutex.ReleaseMutex();
            }
        }
    }

    private static bool ConfigureInitialCompressionBatch(
        string actionId,
        ConversionBatchSession session,
        AppLogger logger)
    {
        var initialItems = session.GetPendingQueueItems();
        if (initialItems.Count == 0)
        {
            return false;
        }

        var emptyOptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (initialItems.Count == 1)
        {
            if (!TryGetCompressionOptions(actionId, initialItems[0].InputPath, logger, out var options))
            {
                return false;
            }

            session.SetSharedOptions(emptyOptions);
            session.SetItemOptions(initialItems[0].QueueItemId, options);
            session.SetLateItemOptionsProvider(item => GetCompressionOptionsResult(actionId, item.InputPath, logger));
            return true;
        }

        CompressMultiFileChoice choice;
        using (var choiceForm = new CompressMultiFileChoiceForm(initialItems.Count))
        {
            if (choiceForm.ShowDialog() != DialogResult.OK)
            {
                return false;
            }

            choice = choiceForm.Choice;
        }

        if (choice == CompressMultiFileChoice.SameForAll)
        {
            if (!TryGetCompressionOptions(actionId, initialItems[0].InputPath, logger, out var sharedOptions))
            {
                return false;
            }

            // Late arrivals intentionally inherit the same shared choice, matching the user's
            // explicit SameForAll decision for this live compression session.
            session.SetSharedOptions(sharedOptions);
            return true;
        }

        session.SetSharedOptions(emptyOptions);
        foreach (var item in initialItems)
        {
            if (!TryGetCompressionOptions(actionId, item.InputPath, logger, out var itemOptions))
            {
                return false;
            }

            session.SetItemOptions(item.QueueItemId, itemOptions);
        }

        // A path received after the initial PerFile sequence needs its own specialized picker
        // when it reaches the queue head; it must not inherit a different occurrence's options.
        session.SetLateItemOptionsProvider(item => GetCompressionOptionsResult(actionId, item.InputPath, logger));
        return true;
    }

    private static bool TryGetCompressionOptions(
        string actionId,
        string inputPath,
        AppLogger logger,
        out Dictionary<string, string> options)
    {
        options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var singlePath = new List<string> { inputPath };
        return actionId switch
        {
            "compress-video" => EnsureCompressVideoOptions(singlePath, options, logger),
            "compress-audio" => EnsureCompressAudioOptions(singlePath, options, logger),
            "compress-image" => EnsureCompressImageOptions(singlePath, options, logger),
            _ => false
        };
    }

    private static ConversionBatchSession.BatchOptionResult GetCompressionOptionsResult(
        string actionId,
        string inputPath,
        AppLogger logger)
    {
        return TryGetCompressionOptions(actionId, inputPath, logger, out var options)
            ? ConversionBatchSession.BatchOptionResult.Succeeded(options)
            : ConversionBatchSession.BatchOptionResult.Failed();
    }

    private static ConversionBatchSession.BatchDefinition? GetCompressionBatchDefinition(string actionId)
    {
        if (actionId.Equals("compress-video", StringComparison.OrdinalIgnoreCase))
        {
            return ConversionBatchSession.CreateCompressVideoDefinition();
        }

        if (actionId.Equals("compress-audio", StringComparison.OrdinalIgnoreCase))
        {
            return ConversionBatchSession.CreateCompressAudioDefinition();
        }

        if (actionId.Equals("compress-image", StringComparison.OrdinalIgnoreCase))
        {
            return ConversionBatchSession.CreateCompressImageDefinition();
        }

        return null;
    }
}
