using FrameShift.Core.AI.Ocr;
using FrameShift.Core.Actions;
using FrameShift.Core.Logging;
using FrameShift.Windows.AI;
using FrameShift.Windows.Batch;
using FrameShift.Windows.Helpers;
using FrameShift.Windows.ProgressUI;

namespace FrameShift;

internal static partial class Program
{
    private static int RunOcrBatch(ActionRegistry registry, AppLogger logger, IReadOnlyList<string> inputPaths, IReadOnlyDictionary<string, string>? options)
    {
        var definition = ConversionBatchSession.CreateExtractTextDefinition();
        using var mutex = new Mutex(true, definition.MutexName, out var primary);
        var owns = primary;
        try
        {
            Dictionary<string, string>? explicitOptions = null;
            if (options is { Count: > 0 })
                explicitOptions = OcrSettings.Parse(options).ToOptions();
            if (!primary)
            {
                // Explorer launches are gathered before the single primary picker appears.
                try
                {
                    if (ConversionBatchSession.SendPathsToPrimaryInstance(definition, inputPaths, explicitOptions))
                        return 0;
                }
                catch (Exception ex)
                {
                    logger.Log($"OCR queue is closing or unavailable: {ex.Message}");
                }

                try
                {
                    owns = mutex.WaitOne(TimeSpan.FromSeconds(5));
                }
                catch (AbandonedMutexException)
                {
                    owns = true;
                }

                if (!owns)
                {
                    ShowCliError("The Extract Text queue could not accept these files. Try again after it closes.");
                    return 1;
                }
            }

            registry.TryGet("extract-text", out var action);
            using var cancellation = new CancellationTokenSource();
            using var session = new ConversionBatchSession(definition, action, logger);
            session.Initialize(inputPaths.ToArray(), cancellation.Token);
            session.WaitForPickerDebounce(cancellation.Token);
            IReadOnlyList<string> Inputs() => session.GetPendingQueueItems().Select(item => item.InputPath).ToArray();
            OcrSettings settings;
            if (explicitOptions is null)
            {
                var preferences = FrameShiftUiSettings.Load();
                var initial = new OcrSettings(Model: preferences.OcrModel == "tiny" ? "tiny" : "small", Format: preferences.OcrFormat is "pdf" or "json" ? preferences.OcrFormat : "txt", Paragraphs: preferences.OcrParagraphs, Dpi: preferences.OcrDpi == 300 ? 300 : 200);
                using var picker = new ExtractTextPickerForm(Inputs, initial);
                if (picker.ShowDialog() != DialogResult.OK || picker.Selection is null)
                    return 0;
                settings = picker.Selection;
            }
            else
            {
                settings = OcrSettings.Parse(explicitOptions);
                if (!EnsureOcrModelReady(Inputs(), settings))
                    return 0;
            }

            session.SetSharedOptions(settings.ToOptions());
            using var progress = new ProgressForm();
            session.AttachProgressForm(progress);
            // Includes explicit options: each later invocation retains its own model/format.
            session.SetItemPreflight(item =>
            {
                var itemSettings = item.Options is { Count: > 0 } ? OcrSettings.Parse(item.Options) : settings;
                using var itemCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
                using var monitorStop = new CancellationTokenSource();
                var monitor = ExtractTextAction.MonitorItemCancellationAsync(progress, item.InputPath, itemCancellation, monitorStop.Token);
                try
                {
                    return EnsureOcrModelReady([item.InputPath], itemSettings, progress, itemCancellation.Token)
                        ? ConversionBatchSession.BatchOptionResult.Succeeded(itemSettings.ToOptions())
                        : ConversionBatchSession.BatchOptionResult.Failed();
                }
                catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
                {
                    return ConversionBatchSession.BatchOptionResult.Failed();
                }
                finally
                {
                    monitorStop.Cancel();
                    monitor.GetAwaiter().GetResult();
                }
            });
            progress.CancelRequested += (_, _) => RequestCancellationAsync(cancellation, logger, "extract-text");
            progress.Shown += (_, _) => session.StartProcessing(cancellation.Token);
            Application.Run(progress);
            session.Close();
            return session.ExitCode;
        }
        catch (Exception ex)
        {
            logger.Log($"ExtractText startup failed: {ex}");
            ShowCliError(ex.Message);
            return 1;
        }
        finally
        {
            if (owns)
            {
                mutex.ReleaseMutex();
                if (registry.TryGet("extract-text", out var action))
                    (action as IDisposable)?.Dispose();
            }
        }
    }

    private static bool EnsureOcrModelReady(IReadOnlyList<string> paths, OcrSettings settings, IWin32Window? owner = null, CancellationToken token = default)
    {
        var model = OcrModelCatalog.Get(settings.Model);
        if (OcrModelCatalog.IsReady(model, token: token))
            return true;
        if (!OcrPreparation.NeedsModel(paths, settings, token, paths.Count > 1))
            return true;
        bool Download()
        {
            using var download = new DownloadModelForm("FrameShift AI - Extract Text", $"Download {model.DisplayName} for local OCR", IconPaths.FrameShiftAiIcon, model.DisplayName, "Apache-2.0", model.SizeBytes, (progress, ct) => OcrModelDownloader.DownloadAsync(model, progress, ct));
            return download.ShowDialog(owner) == DialogResult.OK;
        }

        token.ThrowIfCancellationRequested();
        return owner is Control control && control.InvokeRequired ? (bool)control.Invoke((Func<bool>)Download) : Download();
    }
}
