using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FrameShift.Core.Actions;
using FrameShift.Core.Logging;
using FrameShift.Windows.Forms;
using FrameShift.Windows.ProgressUI;

namespace FrameShift.Windows.Batch;

internal sealed class ConversionBatchSession
{
    private const int PickerDebounceMilliseconds = 700;
    private const int IdleCloseMilliseconds = 2500;

    private readonly BatchDefinition _definition;
    private readonly IFrameShiftAction _action;
    private readonly AppLogger _logger;
    private readonly ConcurrentQueue<BatchQueueItem> _pendingPaths = new();
    private readonly Dictionary<string, BatchQueueItem> _knownItems = new(StringComparer.Ordinal);
    private readonly HashSet<string> _removedItems = new(StringComparer.Ordinal);
    private readonly object _sync = new();
    private readonly QueueSignalState _queueState = new();
    private long _queueItemSequence;

    private Dictionary<string, string>? _sharedOptions;
    private Thread? _pipeServerThread;
    private CancellationToken _sessionCancellationToken;
    private string? _currentInputPath;
    private bool _globalCancelRequested;
    private int _globalCancelDiagnosticScheduled;
    private bool _pickerHandled;
    private bool _closing;
    private bool _closeModeActivated;
    private ProgressForm? _progressForm;

    public ConversionBatchSession(
        BatchDefinition definition,
        IFrameShiftAction action,
        AppLogger logger,
        ProgressForm? progressForm = null)
    {
        _definition = definition;
        _action = action;
        _logger = logger;
        AttachProgressForm(progressForm);
    }

    public int ExitCode { get; private set; }

    public void Start(string[] initialPaths, CancellationToken cancellationToken)
    {
        _logger.Log($"ConversionBatchSession: Start entered. actionId={_definition.ActionId}, initialPathCount={initialPaths.Length}.");
        Initialize(initialPaths, cancellationToken);
        StartProcessing(cancellationToken);
        _logger.Log("ConversionBatchSession: Start exiting.");
    }

    public void Initialize(string[] initialPaths, CancellationToken cancellationToken)
    {
        _logger.Log($"ConversionBatchSession: Initialize entered. actionId={_definition.ActionId}, initialPathCount={initialPaths.Length}.");
        _sessionCancellationToken = cancellationToken;
        StartPipeServer();
        EnqueuePaths(initialPaths);
        _logger.Log("ConversionBatchSession: Initialize exiting.");
    }

    public void StartProcessing(CancellationToken cancellationToken)
    {
        _logger.Log($"ConversionBatchSession: StartProcessing entered. actionId={_definition.ActionId}.");
        _ = Task.Run(() => ProcessLoopAsync(cancellationToken), cancellationToken);
        _logger.Log("ConversionBatchSession: StartProcessing exiting.");
    }

    public void SetSharedOptions(IReadOnlyDictionary<string, string> options)
    {
        _sharedOptions = new Dictionary<string, string>(options, StringComparer.OrdinalIgnoreCase);
        _pickerHandled = true;
    }

    public void AttachProgressForm(ProgressForm? progressForm)
    {
        if (progressForm is null)
        {
            return;
        }

        if (ReferenceEquals(_progressForm, progressForm))
        {
            return;
        }

        if (_progressForm is not null)
        {
            _progressForm.QueueItemRemoveRequested -= OnQueueItemRemoveRequested;
            _progressForm.CancelRequested -= OnCancelRequested;
        }

        _progressForm = progressForm;
        _progressForm.QueueItemRemoveRequested += OnQueueItemRemoveRequested;
        _progressForm.CancelRequested += OnCancelRequested;

        foreach (var item in GetPendingPathsSnapshot())
        {
            _progressForm.AddQueueItem(item.QueueItemId, item.InputPath);
        }
    }

    public static void SendPathsToPrimaryInstance(
        BatchDefinition definition,
        IEnumerable<string> inputPaths,
        IReadOnlyDictionary<string, string>? options = null)
    {
        using var pipeClient = new NamedPipeClientStream(".", definition.PipeName, PipeDirection.Out);
        pipeClient.Connect(5000);

        using var writer = new StreamWriter(pipeClient, Encoding.UTF8)
        {
            AutoFlush = true
        };

        foreach (var inputPath in inputPaths)
        {
            if (!string.IsNullOrWhiteSpace(inputPath))
            {
                // One JSON object per line carries the path together with the per-launch
                // CLI options (e.g. --rmbg-model), so the receiving session does not fall
                // back to its own shared options for queued items.
                writer.WriteLine(FormatQueueMessage(inputPath, options));
            }
        }
    }

    // Wire format helpers. Each queue message is a single-line JSON object. A bare path line
    // (no leading '{') is still accepted for backward compatibility with older callers.
    private sealed record QueueMessage(
        [property: JsonPropertyName("path")] string Path,
        [property: JsonPropertyName("options")] Dictionary<string, string>? Options);

    private static readonly JsonSerializerOptions s_queueMessageJsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    internal static string FormatQueueMessage(string inputPath, IReadOnlyDictionary<string, string>? options)
    {
        var optionsCopy = options is { Count: > 0 }
            ? new Dictionary<string, string>(options)
            : null;
        return JsonSerializer.Serialize(new QueueMessage(inputPath, optionsCopy), s_queueMessageJsonOptions);
    }

    internal static bool TryParseQueueMessage(
        string? line,
        out string inputPath,
        out IReadOnlyDictionary<string, string>? options)
    {
        inputPath = string.Empty;
        options = null;

        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var trimmed = line.Trim();
        if (trimmed.StartsWith('{'))
        {
            try
            {
                var message = JsonSerializer.Deserialize<QueueMessage>(trimmed, s_queueMessageJsonOptions);
                if (message is not null && !string.IsNullOrWhiteSpace(message.Path))
                {
                    inputPath = message.Path;
                    options = message.Options is { Count: > 0 }
                        ? new Dictionary<string, string>(message.Options, StringComparer.OrdinalIgnoreCase)
                        : null;
                    return true;
                }
            }
            catch (JsonException)
            {
                // Fall through and treat the line as a bare path (backward compatibility).
            }
        }

        // Backward compatibility: a plain path line with no options.
        inputPath = trimmed;
        return true;
    }

    public static BatchDefinition CreateVideoDefinition() => new(
        ActionId: "convert-video",
        DisplayName: "Convert Video",
        RequiresSharedOptions: true,
        DefaultOptions: null,
        PickerTitle: "FrameShift - Convert Video",
        PickerDescription: "Select a target container and a profile.",
        SupportedSourceFormatsText: VideoConversionCatalog.GetSupportedSourceFormatsText(),
        MutexName: @"Local\FrameShift_ConvertVideoBatch",
        PipeName: "FrameShift_ConvertVideoBatchQueue",
        ShowProfiles: true,
        IsSupportedSourceExtension: VideoConversionCatalog.IsSupportedSourceExtension,
        GetTargetsForSelection: inputPaths =>
        {
            var targets = VideoConversionCatalog.GetTargets();
            if (inputPaths.Select(path => Path.GetExtension(path)).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            {
                return targets.Where(target => !string.Equals(target.Id, "remux", StringComparison.OrdinalIgnoreCase)).ToArray();
            }

            return targets;
        },
        GetProfiles: VideoConversionCatalog.GetProfiles);

    public static BatchDefinition CreateAudioDefinition() => new(
        ActionId: "convert-audio",
        DisplayName: "Convert Audio",
        RequiresSharedOptions: true,
        DefaultOptions: null,
        PickerTitle: "FrameShift - Convert Audio",
        PickerDescription: "Select an output format and a profile.",
        SupportedSourceFormatsText: AudioConversionCatalog.GetSupportedSourceFormatsText(),
        MutexName: @"Local\FrameShift_ConvertAudioBatch",
        PipeName: "FrameShift_ConvertAudioBatchQueue",
        ShowProfiles: true,
        IsSupportedSourceExtension: AudioConversionCatalog.IsSupportedSourceExtension,
        GetTargetsForSelection: _ => AudioConversionCatalog.GetTargets(),
        GetProfiles: AudioConversionCatalog.GetProfiles);

    public static BatchDefinition CreateImageDefinition() => new(
        ActionId: "convert-image",
        DisplayName: "Convert Image",
        RequiresSharedOptions: true,
        DefaultOptions: null,
        PickerTitle: "FrameShift - Convert Image",
        PickerDescription: "Select an output format and a profile.",
        SupportedSourceFormatsText: ImageConversionCatalog.GetSupportedSourceFormatsText(),
        MutexName: @"Local\FrameShift_ConvertImageBatch",
        PipeName: "FrameShift_ConvertImageBatchQueue",
        ShowProfiles: true,
        IsSupportedSourceExtension: ImageConversionCatalog.IsSupportedSourceExtension,
        GetTargetsForSelection: _ => ImageConversionCatalog.GetTargets(),
        GetProfiles: ImageConversionCatalog.GetProfiles);

    public static BatchDefinition CreateExtractFramesDefinition() => new(
        ActionId: "extract-frames",
        DisplayName: "Extract Frames",
        RequiresSharedOptions: false,
        DefaultOptions: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
        PickerTitle: null,
        PickerDescription: null,
        SupportedSourceFormatsText: VideoConversionCatalog.GetSupportedSourceFormatsText(),
        MutexName: @"Local\FrameShift_ExtractFramesBatch",
        PipeName: "FrameShift_ExtractFramesBatchQueue",
        ShowProfiles: false,
        IsSupportedSourceExtension: VideoConversionCatalog.IsSupportedSourceExtension,
        GetTargetsForSelection: _ => [],
        GetProfiles: static () => [],
        PrimaryButtonText: "Extract");

    public static BatchDefinition CreateExtractAudioDefinition() => new(
        ActionId: "extract-audio",
        DisplayName: "Extract Audio",
        RequiresSharedOptions: true,
        DefaultOptions: null,
        PickerTitle: "FrameShift - Extract Audio",
        PickerDescription: "Choose the output audio format.",
        SupportedSourceFormatsText: VideoConversionCatalog.GetSupportedSourceFormatsText(),
        MutexName: @"Local\FrameShift_ExtractAudioBatch",
        PipeName: "FrameShift_ExtractAudioBatchQueue",
        ShowProfiles: false,
        IsSupportedSourceExtension: VideoConversionCatalog.IsSupportedSourceExtension,
        GetTargetsForSelection: _ => ExtractAudioCatalog.GetTargets(),
        GetProfiles: static () => [],
        PrimaryButtonText: "Extract");

    public static BatchDefinition CreateRemoveBackgroundDefinition() => new(
        ActionId: "remove-background",
        DisplayName: "Remove Background",
        RequiresSharedOptions: false,
        DefaultOptions: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
        PickerTitle: null,
        PickerDescription: null,
        SupportedSourceFormatsText: ".png, .jpg, .jpeg, .webp, .bmp",
        MutexName: @"Local\FrameShift_RemoveBackgroundBatch",
        PipeName: "FrameShift_RemoveBackgroundBatchQueue",
        ShowProfiles: false,
        IsSupportedSourceExtension: static extension =>
            extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".webp", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase),
        GetTargetsForSelection: _ => [],
        GetProfiles: static () => [],
        KeepWindowOpenOnFailure: true,
        PrimaryButtonText: "Process");

    public static BatchDefinition CreateUpscaleImageDefinition() => new(
        ActionId: "upscale-image",
        DisplayName: "Upscale Image",
        RequiresSharedOptions: false,
        DefaultOptions: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
        PickerTitle: null,
        PickerDescription: null,
        SupportedSourceFormatsText: ".png, .jpg, .jpeg, .webp, .bmp",
        MutexName: @"Local\FrameShift_UpscaleImageBatch",
        PipeName: "FrameShift_UpscaleImageBatchQueue",
        ShowProfiles: false,
        IsSupportedSourceExtension: static extension =>
            extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".webp", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase),
        GetTargetsForSelection: _ => [],
        GetProfiles: static () => [],
        KeepWindowOpenOnFailure: true,
        PrimaryButtonText: "Upscale");

    public static BatchDefinition CreateUpscaleVideoDefinition() => new(
        ActionId: "upscale-video",
        DisplayName: "Upscale Video",
        RequiresSharedOptions: false,
        DefaultOptions: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
        PickerTitle: null,
        PickerDescription: null,
        SupportedSourceFormatsText: ".mp4, .mkv, .avi, .mov, .webm, .m4v",
        MutexName: @"Local\FrameShift_UpscaleVideoBatch",
        PipeName: "FrameShift_UpscaleVideoBatchQueue",
        ShowProfiles: false,
        IsSupportedSourceExtension: static extension =>
            extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".mkv", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".avi", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".mov", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".webm", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".m4v", StringComparison.OrdinalIgnoreCase),
        GetTargetsForSelection: _ => [],
        GetProfiles: static () => [],
        KeepWindowOpenOnFailure: true,
        PrimaryButtonText: "Upscale");

    public static BatchDefinition CreateSeparateAudioDefinition() => new(
        ActionId: "separate-audio",
        DisplayName: "Audio Separation",
        RequiresSharedOptions: false,
        DefaultOptions: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
        PickerTitle: null,
        PickerDescription: null,
        SupportedSourceFormatsText: FrameShift.Core.AI.SeparateAudio.SeparateAudioAction.GetSupportedExtensionsText(),
        MutexName: @"Local\FrameShift_SeparateAudioBatch",
        PipeName: "FrameShift_SeparateAudioBatchQueue",
        ShowProfiles: false,
        IsSupportedSourceExtension: static extension =>
            extension.Equals(".wav", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".flac", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".m4a", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".ogg", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".aac", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".wma", StringComparison.OrdinalIgnoreCase),
        GetTargetsForSelection: _ => [],
        GetProfiles: static () => [],
        KeepWindowOpenOnFailure: true,
        PrimaryButtonText: "Separate");

    public static BatchDefinition CreateRemoveNoiseDefinition() => new(
        ActionId: "remove-noise",
        DisplayName: "Remove Noise",
        RequiresSharedOptions: false,
        DefaultOptions: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
        PickerTitle: null,
        PickerDescription: null,
        SupportedSourceFormatsText: FrameShift.Core.AI.RemoveNoise.RemoveNoiseAction.GetSupportedExtensionsText(),
        MutexName: @"Local\FrameShift_RemoveNoiseBatch",
        PipeName: "FrameShift_RemoveNoiseBatchQueue",
        ShowProfiles: false,
        IsSupportedSourceExtension: static extension =>
            extension.Equals(".wav", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".flac", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".ogg", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".m4a", StringComparison.OrdinalIgnoreCase),
        GetTargetsForSelection: _ => [],
        GetProfiles: static () => [],
        KeepWindowOpenOnFailure: true,
        PrimaryButtonText: "Denoise");

    public static BatchDefinition CreateRemoveNoiseVideoDefinition() => new(
        ActionId: "remove-noise-video",
        DisplayName: "Remove Noise (Video)",
        RequiresSharedOptions: false,
        DefaultOptions: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
        PickerTitle: null,
        PickerDescription: null,
        SupportedSourceFormatsText: FrameShift.Core.AI.RemoveNoise.RemoveNoiseVideoSettings.GetSupportedVideoExtensionsText(),
        MutexName: @"Local\FrameShift_RemoveNoiseVideoBatch",
        PipeName: "FrameShift_RemoveNoiseVideoBatchQueue",
        ShowProfiles: false,
        IsSupportedSourceExtension: static extension =>
            extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".mkv", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".avi", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".mov", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".webm", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".m4v", StringComparison.OrdinalIgnoreCase),
        GetTargetsForSelection: _ => [],
        GetProfiles: static () => [],
        KeepWindowOpenOnFailure: true,
        PrimaryButtonText: "Denoise");

    private async Task ProcessLoopAsync(CancellationToken cancellationToken)
    {
        _logger.Log("ConversionBatchSession: queue loop entered.");
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (!_pickerHandled)
                {
                    if (!_definition.RequiresSharedOptions)
                    {
                        _sharedOptions = _definition.DefaultOptions is null
                            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                            : new Dictionary<string, string>(_definition.DefaultOptions, StringComparer.OrdinalIgnoreCase);
                        _pickerHandled = true;
                        continue;
                    }

                    if (_pendingPaths.IsEmpty)
                    {
                        if (ShouldCloseForIdle())
                        {
                            CloseProgressWindow();
                            return;
                        }

                        await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    if (!IsPickerDebounceElapsed())
                    {
                        await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    var optionResult = PromptForSharedOptions();
                    _pickerHandled = true;

                    if (!optionResult.Success)
                    {
                        CloseProgressWindow();
                        return;
                    }

                    _sharedOptions = optionResult.Options;
                }

                if (_pendingPaths.TryDequeue(out var queueItem))
                {
                    if (IsRemoved(queueItem.QueueItemId))
                    {
                        continue;
                    }

                    _currentInputPath = queueItem.InputPath;
                    await ProcessQueuedPathAsync(queueItem, cancellationToken).ConfigureAwait(false);
                    _currentInputPath = null;
                    continue;
                }

                if (ShouldCloseForIdle())
                {
                    if (_definition.KeepWindowOpenOnFailure &&
                        ExitCode != 0 &&
                        !_globalCancelRequested)
                    {
                        EnableCloseModeForFailure();
                        return;
                    }

                    CloseProgressWindow();
                    return;
                }

                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }

            CloseProgressWindow();
        }
        catch (OperationCanceledException)
        {
            _logger.Log("ConversionBatchSession: OperationCanceledException catch entered.");
            CloseProgressWindow();
        }
        catch (Exception ex)
        {
            _logger.Log($"ConversionBatchSession: generic exception catch entered: {ex}");
            ExitCode = 1;
            _logger.Log(ex.ToString());
            _progressForm?.ReportState("failed", $"Batch {_definition.DisplayName.ToLowerInvariant()} failed.");
            CloseProgressWindow();
        }
        finally
        {
            _logger.Log("ConversionBatchSession: finally entered.");
            _logger.Log("ConversionBatchSession: listener stop/dispose started.");
            _logger.Log("ConversionBatchSession: listener stop/dispose finished.");
        }
    }

    private async Task ProcessQueuedPathAsync(BatchQueueItem queueItem, CancellationToken cancellationToken)
    {
        var inputPath = queueItem.InputPath;
        _logger.Log($"ConversionBatchSession: item started. inputPath={inputPath}");
        if (!_definition.IsSupportedSourceExtension(Path.GetExtension(inputPath)))
        {
            ExitCode = 1;
            ReportQueueItem(
                inputPath,
                "failed",
                MediaActionMessages.UnsupportedSourceFormat(
                    Path.GetExtension(inputPath),
                    _definition.SupportedSourceFormatsText));
            return;
        }

        if (_sharedOptions is null)
        {
            ExitCode = 1;
            ReportQueueItem(inputPath, "failed", "Missing action options.");
            return;
        }

        // Per-item options (carried over the pipe from the launch that queued this file) win
        // over the session's shared options, so each queued item runs with its own model.
        var effectiveOptions = MergeOptions(_sharedOptions, queueItem.Options);
        var request = new ActionRequest(inputPath, _logger, _progressForm, effectiveOptions);
        ReportQueueItem(inputPath, "processing", "Starting.");
        var result = await _action.ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
        _logger.Log($"ConversionBatchSession: item returned from action.ExecuteAsync. inputPath={inputPath}, success={result.Success}, canceled={result.Canceled}, scope={result.CancellationScope}, message={result.Message}");

        if (result.Canceled)
        {
            _logger.Log($"ConversionBatchSession: result canceled. inputPath={inputPath}");
            ReportQueueItem(inputPath, "canceled", result.Message);
            return;
        }

        if (result.Success)
        {
            _logger.Log($"ConversionBatchSession: result success. inputPath={inputPath}");
            ReportQueueItem(inputPath, "done", result.Message);
            return;
        }

        _logger.Log($"ConversionBatchSession: result failure. inputPath={inputPath}");
        ReportQueueItem(inputPath, "failed", result.Message);

        if (!result.Success && !result.Canceled)
        {
            ExitCode = 1;
        }
    }

    internal static IReadOnlyDictionary<string, string> MergeOptions(
        IReadOnlyDictionary<string, string> sharedOptions,
        IReadOnlyDictionary<string, string>? itemOptions)
    {
        var merged = new Dictionary<string, string>(sharedOptions, StringComparer.OrdinalIgnoreCase);
        if (itemOptions is not null)
        {
            foreach (var kv in itemOptions)
            {
                merged[kv.Key] = kv.Value;
            }
        }

        return merged;
    }

    public void WaitForPickerDebounce(CancellationToken cancellationToken)
    {
        while (!IsPickerDebounceElapsed())
        {
            cancellationToken.ThrowIfCancellationRequested();
            Thread.Sleep(50);
        }
    }

    internal BatchOptionResult PromptForSharedOptions(IWin32Window? owner = null)
    {
        var pendingPaths = GetPendingPathsSnapshot();
        var supportedPaths = pendingPaths
            .Where(item => _definition.IsSupportedSourceExtension(Path.GetExtension(item.InputPath)))
            .ToArray();

        if (supportedPaths.Length == 0)
        {
            ExitCode = 1;
            foreach (var pendingPath in pendingPaths)
            {
                ReportQueueItem(
                    pendingPath.InputPath,
                    "failed",
                    MediaActionMessages.UnsupportedSourceFormat(
                        Path.GetExtension(pendingPath.InputPath),
                        _definition.SupportedSourceFormatsText));
            }

            return BatchOptionResult.Failed();
        }

        var targets = _definition.GetTargetsForSelection(supportedPaths.Select(item => item.InputPath).ToArray());
        if (targets.Count == 0)
        {
            ExitCode = 1;
            foreach (var supportedPath in supportedPaths)
            {
                ReportQueueItem(
                    supportedPath.InputPath,
                    "failed",
                    MediaActionMessages.NoAlternativeTargetFormatsAvailable());
            }

            return BatchOptionResult.Failed();
        }

        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var sourceLabel = supportedPaths.Length == 1
            ? FormatSelectionLabel(Path.GetFileName(supportedPaths[0].InputPath))
            : $"{supportedPaths.Length} selected files";

        ConversionSelection? selection = null;
        void ShowPicker()
        {
            using var picker = new ConversionPickerForm(
                _definition.PickerTitle!,
                sourceLabel,
                _definition.PickerDescription!,
                targets,
                _definition.GetProfiles(),
                targets[0].Id,
                _definition.ShowProfiles ? _definition.GetProfiles().FirstOrDefault()?.Id : null,
                _definition.PrimaryButtonText);

            var dialogResult = owner is null
                ? picker.ShowDialog()
                : picker.ShowDialog(owner);

            if (dialogResult == DialogResult.OK)
            {
                selection = picker.Selection;
            }
        }

        if (_progressForm is not null && _progressForm.IsHandleCreated && _progressForm.InvokeRequired)
        {
            _progressForm.Invoke((Action)ShowPicker);
        }
        else
        {
            ShowPicker();
        }

        if (selection is null)
        {
            return BatchOptionResult.Failed();
        }

        options[ActionOptionKeys.Target] = selection.TargetId;
        if (_definition.ShowProfiles && !string.IsNullOrWhiteSpace(selection.ProfileId))
        {
            options[ActionOptionKeys.Profile] = selection.ProfileId!;
        }

        return BatchOptionResult.Succeeded(options);
    }

    private BatchQueueItem[] GetPendingPathsSnapshot()
    {
        lock (_sync)
        {
            return _knownItems.Values
                .Where(item => !_removedItems.Contains(item.QueueItemId))
                .OrderBy(item => item.Sequence)
                .ToArray();
        }
    }

    private bool IsPickerDebounceElapsed()
    {
        var elapsed = DateTime.UtcNow - new DateTime(Interlocked.Read(ref _queueState.LastActivityTicks), DateTimeKind.Utc);
        return elapsed.TotalMilliseconds >= PickerDebounceMilliseconds;
    }

    private bool ShouldCloseForIdle()
    {
        var elapsed = DateTime.UtcNow - new DateTime(Interlocked.Read(ref _queueState.LastActivityTicks), DateTimeKind.Utc);
        return _pickerHandled && _pendingPaths.IsEmpty && elapsed.TotalMilliseconds >= IdleCloseMilliseconds;
    }

    private void StartPipeServer()
    {
        _logger.Log("ConversionBatchSession: batch listener started.");
        var serverThread = new Thread(() =>
        {
            _logger.Log("ConversionBatchSession: pipe listener thread entered.");
            while (!_closing)
            {
                try
                {
                    using var pipeServer = new NamedPipeServerStream(
                        _definition.PipeName,
                        PipeDirection.In,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    pipeServer.WaitForConnection();

                    using var reader = new StreamReader(pipeServer, Encoding.UTF8);
                    string? line;
                    while ((line = reader.ReadLine()) is not null)
                    {
                        if (TryParseQueueMessage(line, out var inputPath, out var itemOptions))
                        {
                            EnqueueItem(inputPath, itemOptions);
                            _logger.Log($"QUEUED FROM SECONDARY INSTANCE ({_definition.ActionId}): {inputPath} [options={DescribeOptions(itemOptions)}]");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Log($"Convert batch pipe server error ({_definition.ActionId}):{Environment.NewLine}{ex}");
                }
            }

            _logger.Log("ConversionBatchSession: pipe listener thread exiting.");
        })
        {
            IsBackground = true
        };

        _pipeServerThread = serverThread;
        serverThread.Start();
    }

    // Items enqueued from the primary launch carry no per-item options: they fall back to the
    // session's shared options, which already are their options.
    private void EnqueuePaths(IEnumerable<string> inputPaths)
    {
        foreach (var inputPath in inputPaths)
        {
            EnqueueItem(inputPath, null);
        }
    }

    private void EnqueueItem(string inputPath, IReadOnlyDictionary<string, string>? options)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            return;
        }

        var queueItem = CreateQueueItem(inputPath, options);
        lock (_sync)
        {
            _knownItems[queueItem.QueueItemId] = queueItem;
            _removedItems.Remove(queueItem.QueueItemId);
        }

        _pendingPaths.Enqueue(queueItem);
        Interlocked.Exchange(ref _queueState.LastActivityTicks, DateTime.UtcNow.Ticks);
        _progressForm?.AddQueueItem(queueItem.QueueItemId, inputPath);
        _logger.Log($"QUEUED ({_definition.ActionId}): {inputPath} [queueItemId={queueItem.QueueItemId}, options={DescribeOptions(options)}]");
    }

    private static string DescribeOptions(IReadOnlyDictionary<string, string>? options)
    {
        if (options is null || options.Count == 0)
        {
            return "<none>";
        }

        return string.Join(", ", options.Select(kv => $"{kv.Key}={kv.Value}"));
    }

    private void OnQueueItemRemoveRequested(object? sender, string queueItemId)
    {
        string? inputPath = null;
        lock (_sync)
        {
            if (!_knownItems.Remove(queueItemId, out var queueItem))
            {
                return;
            }

            inputPath = queueItem.InputPath;
            _removedItems.Add(queueItemId);
        }

        _logger.Log($"REMOVED FROM QUEUE ({_definition.ActionId}): {inputPath} [queueItemId={queueItemId}]");
    }

    private void OnCancelRequested(object? sender, EventArgs e)
    {
        _globalCancelRequested = true;
        _logger.Log("ConversionBatchSession: OnCancelRequested entered.");
        var canceledBeforeProcessing = CancelPendingPaths();
        _logger.Log($"ConversionBatchSession: OnCancelRequested canceled pending items count={canceledBeforeProcessing.Length}.");
        foreach (var item in canceledBeforeProcessing)
        {
            ReportQueueItem(item.InputPath, "canceled", MediaActionMessages.CanceledBeforeProcessing());
        }

        ScheduleGlobalCancelDiagnostics("cancel-requested");
    }

    private bool IsRemoved(string queueItemId)
    {
        lock (_sync)
        {
            if (_removedItems.Remove(queueItemId))
            {
                return true;
            }
        }

        return false;
    }

    private BatchQueueItem[] CancelPendingPaths()
    {
        lock (_sync)
        {
            var pendingPaths = _pendingPaths
                .ToArray()
                .Where(item => !string.IsNullOrWhiteSpace(item.InputPath))
                .ToArray();

            while (_pendingPaths.TryDequeue(out _))
            {
            }

            foreach (var item in pendingPaths)
            {
                _removedItems.Add(item.QueueItemId);
            }

            return pendingPaths;
        }
    }

    private static string FormatSelectionLabel(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return "selected file";
        }

        const int maxLength = 72;
        if (fileName.Length <= maxLength)
        {
            return fileName;
        }

        var prefixLength = 34;
        var suffixLength = 30;
        return $"{fileName[..prefixLength]}...{fileName[^suffixLength..]}";
    }

    private void CloseProgressWindow()
    {
        _logger.Log("ConversionBatchSession: CloseProgressWindow called.");
        if (_closing)
        {
            _logger.Log("ConversionBatchSession: CloseProgressWindow returned early because closing is already true.");
            return;
        }

        _closing = true;
        _progressForm?.CloseSafely();
        _logger.Log("ConversionBatchSession: CloseProgressWindow returned.");
    }

    private void EnableCloseModeForFailure()
    {
        if (_closeModeActivated)
        {
            return;
        }

        _closeModeActivated = true;
        _closing = true;
        _progressForm?.EnableCloseMode("Completed with errors. Review the queue for details.");
        _logger.Log("ConversionBatchSession: failure close mode enabled.");
    }

    private void ScheduleGlobalCancelDiagnostics(string reason)
    {
        if (!_globalCancelRequested)
        {
            return;
        }

        if (Interlocked.Exchange(ref _globalCancelDiagnosticScheduled, 1) != 0)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                if (_progressForm is null)
                {
                    _logger.Log($"ConversionBatchSession: global cancel watchdog skipped because progress form is not attached. reason={reason}");
                    return;
                }

                if (_progressForm.IsDisposed)
                {
                    _logger.Log($"ConversionBatchSession: global cancel watchdog skipped because form is already disposed. reason={reason}");
                    return;
                }

                _logger.Log(
                    $"ConversionBatchSession: global cancel watchdog fired. reason={reason}, queueCount={_pendingPaths.Count}, currentItem={_currentInputPath ?? "<none>"}, currentItemState={_progressForm.GetCurrentProcessingState()}, cancellationTokenState={_sessionCancellationToken.IsCancellationRequested}, formIsDisposed={_progressForm.IsDisposed}, formIsHandleCreated={_progressForm.IsHandleCreated}, formInvokeRequired={_progressForm.InvokeRequired}, queueRowCount={_progressForm.GetQueueRowCount()}.");
            }
            catch (Exception ex)
            {
                _logger.Log($"ConversionBatchSession: global cancel watchdog failed: {ex}");
            }
        });
    }

    private void ReportQueueItem(string inputPath, string state, string? message)
    {
        _progressForm?.ReportQueueItem(inputPath, state, message);
    }

    private BatchQueueItem CreateQueueItem(string inputPath, IReadOnlyDictionary<string, string>? options)
    {
        var sequence = Interlocked.Increment(ref _queueItemSequence);
        return new BatchQueueItem(
            $"{_definition.ActionId}:{sequence}",
            inputPath,
            sequence,
            options);
    }

    public sealed record BatchDefinition(
        string ActionId,
        string DisplayName,
        bool RequiresSharedOptions,
        IReadOnlyDictionary<string, string>? DefaultOptions,
        string? PickerTitle,
        string? PickerDescription,
        string SupportedSourceFormatsText,
        string MutexName,
        string PipeName,
        bool ShowProfiles,
        Func<string, bool> IsSupportedSourceExtension,
        Func<IReadOnlyList<string>, IReadOnlyList<IConversionChoice>> GetTargetsForSelection,
        Func<IReadOnlyList<IConversionChoice>> GetProfiles,
        bool KeepWindowOpenOnFailure = false,
        string PrimaryButtonText = "Convert");

    private sealed class QueueSignalState
    {
        public long LastActivityTicks = DateTime.UtcNow.Ticks;
    }

    internal sealed record BatchOptionResult(bool Success, Dictionary<string, string>? Options)
    {
        public static BatchOptionResult Failed() => new(false, null);

        public static BatchOptionResult Succeeded(Dictionary<string, string> options) => new(true, options);
    }

    private sealed record BatchQueueItem(
        string QueueItemId,
        string InputPath,
        long Sequence,
        IReadOnlyDictionary<string, string>? Options);
}
