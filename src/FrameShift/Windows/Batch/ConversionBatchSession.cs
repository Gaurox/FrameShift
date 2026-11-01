using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FrameShift.Core.Actions;
using FrameShift.Core.Logging;
using FrameShift.Core.Helpers;
using FrameShift.Windows.Forms;
using FrameShift.Windows.ProgressUI;

namespace FrameShift.Windows.Batch;

internal sealed class ConversionBatchSession : IDisposable
{
    private const int PickerDebounceMilliseconds = 700;
    private const int IdleCloseMilliseconds = 2500;

    private readonly BatchDefinition _definition;
    private readonly TimeSpan _pipeTimeout;
    private readonly IFrameShiftAction _action;
    private readonly AppLogger _logger;
    private readonly ConcurrentQueue<BatchQueueItem> _pendingPaths = new();
    private readonly Dictionary<string, BatchQueueItem> _knownItems = new(StringComparer.Ordinal);
    private readonly HashSet<string> _removedItems = new(StringComparer.Ordinal);
    private readonly HashSet<string> _acceptedInvocationIds = new(StringComparer.Ordinal);
    private readonly object _sync = new();
    private readonly QueueSignalState _queueState = new();
    private long _queueItemSequence;

    private Dictionary<string, string>? _sharedOptions;
    private Func<BatchQueueItemInfo, BatchOptionResult>? _lateItemOptionsProvider;
    private Func<BatchQueueItemInfo, BatchOptionResult>? _itemPreflight;
    private Thread? _pipeServerThread;
    private NamedPipeServerStream? _activePipeServer;
    private CancellationToken _sessionCancellationToken;
    private string? _currentInputPath;
    private bool _globalCancelRequested;
    private int _globalCancelDiagnosticScheduled;
    private bool _pickerHandled;
    private bool _closing;
    private bool _closeModeActivated;
    private ProgressForm? _progressForm;
    private Task? _processingTask;

    public ConversionBatchSession(
        BatchDefinition definition,
        IFrameShiftAction action,
        AppLogger logger,
        ProgressForm? progressForm = null, TimeSpan? pipeTimeout = null)
    {
        _definition = definition;
        _pipeTimeout = pipeTimeout ?? LocalPipeTransport.ConnectionTimeout;
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

    public Task StartProcessing(CancellationToken cancellationToken)
    {
        _logger.Log($"ConversionBatchSession: StartProcessing entered. actionId={_definition.ActionId}.");
        _processingTask = Task.Run(() => ProcessLoopAsync(cancellationToken), cancellationToken);
        _logger.Log("ConversionBatchSession: StartProcessing exiting.");
        return _processingTask;
    }

    public void SetSharedOptions(IReadOnlyDictionary<string, string> options)
    {
        _sharedOptions = new Dictionary<string, string>(options, StringComparer.OrdinalIgnoreCase);
        _pickerHandled = true;
    }

    public void SetLateItemOptionsProvider(Func<BatchQueueItemInfo, BatchOptionResult>? provider)
    {
        _lateItemOptionsProvider = provider;
    }

    internal void SetItemPreflight(Func<BatchQueueItemInfo, BatchOptionResult> provider) => _itemPreflight = provider;

    public void SetItemOptions(string queueItemId, IReadOnlyDictionary<string, string> options)
    {
        lock (_sync)
        {
            if (_removedItems.Contains(queueItemId) ||
                !_knownItems.TryGetValue(queueItemId, out var item))
            {
                return;
            }

            _knownItems[queueItemId] = item with
            {
                Options = new Dictionary<string, string>(options, StringComparer.OrdinalIgnoreCase)
            };
        }
    }

    internal IReadOnlyList<BatchQueueItemInfo> GetPendingQueueItems()
    {
        lock (_sync)
        {
            return _pendingPaths
                .ToArray()
                .Select(item => _knownItems.TryGetValue(item.QueueItemId, out var current) ? current : item)
                .Where(item => !_removedItems.Contains(item.QueueItemId))
                .OrderBy(item => item.Sequence)
                .Select(ToQueueItemInfo)
                .ToArray();
        }
    }

    internal Task? ProcessingTask => _processingTask;

    public void Close()
    {
        BeginClosing();
    }

    public void Dispose()
    {
        Close();
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

    public static bool SendPathsToPrimaryInstance(
        BatchDefinition definition, IEnumerable<string> inputPaths,
        IReadOnlyDictionary<string, string>? options = null) =>
        SendPathsToPrimaryInstanceAsync(definition, inputPaths, options, CancellationToken.None).GetAwaiter().GetResult();

    internal static async Task<bool> SendPathsToPrimaryInstanceAsync(BatchDefinition definition,
        IEnumerable<string> inputPaths, IReadOnlyDictionary<string, string>? options, CancellationToken token,
        TimeSpan? timeout = null)
    {
        var paths = inputPaths.Where(path => !string.IsNullOrWhiteSpace(path)).Take(LocalPipeTransport.MaximumPaths + 1).ToArray();
        if (paths.Length == 0) return false;
        LocalPipeTransport.ValidatePaths(paths);
        var copy = options is { Count: > 0 } ? new Dictionary<string, string>(options) : null;
        var invocation = new QueueInvocationMessage(Guid.NewGuid().ToString("N"), paths.Select(path => new QueueMessage(path, copy)).ToArray());
        var message = LocalPipeTransport.Serialize(invocation, s_queueMessageJsonOptions);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(timeout ?? LocalPipeTransport.ConnectionTimeout);
        using var pipeClient = LocalPipeTransport.CreateClient(definition.PipeName, PipeDirection.InOut);
        await pipeClient.ConnectAsync(deadline.Token).ConfigureAwait(false);
        await LocalPipeTransport.WriteAsync(pipeClient, message, deadline.Token).ConfigureAwait(false);
        var responseLine = await LocalPipeTransport.ReadAsync(pipeClient, deadline.Token).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(responseLine)) throw new IOException("The primary session closed before confirming the queue invocation.");
        try { return JsonSerializer.Deserialize<QueueInvocationResponse>(responseLine, s_queueMessageJsonOptions)?.Accepted == true; }
        catch (JsonException ex) { throw new IOException("The primary session returned an invalid queue confirmation.", ex); }
    }

    // Wire format helpers. Each queue message is a single-line JSON object. A bare path line
    // (no leading '{') is still accepted for backward compatibility with older callers.
    private sealed record QueueMessage(
        [property: JsonPropertyName("path")] string Path,
        [property: JsonPropertyName("options")] Dictionary<string, string>? Options);

    private sealed record QueueInvocationMessage(
        [property: JsonPropertyName("invocationId")] string InvocationId,
        [property: JsonPropertyName("items")] QueueMessage[] Items);

    private sealed record QueueInvocationResponse(
        [property: JsonPropertyName("accepted")] bool Accepted);

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
                return false;
            }
            return false;
        }

        // Backward compatibility: a plain path line with no options.
        inputPath = trimmed;
        return true;
    }

    public static BatchDefinition CreateExtractTextDefinition() => new(
        ActionId: "extract-text", DisplayName: L.Get("ConversionBatchSession_Extract_Text_9b5993ad"), RequiresSharedOptions: false,
        DefaultOptions: new Dictionary<string,string>(), PickerTitle: null, PickerDescription: null,
        SupportedSourceFormatsText: L.Get("ConversionBatchSession_PNG_JPG_JPEG_WebP_BMP_and_PDF_5fd04462"),
        MutexName: @"Local\FrameShift_ExtractTextBatch", PipeName: "FrameShift_ExtractTextBatchQueue",
        ShowProfiles: false, IsSupportedSourceExtension: Core.AI.Ocr.ExtractTextAction.Supports,
        GetTargetsForSelection: _ => [], GetProfiles: () => [], KeepWindowOpenOnFailure: true, PrimaryButtonText: L.Get("ConversionBatchSession_Extract_Text_9b5993ad"));

    public static BatchDefinition CreateVideoDefinition() => new(
        ActionId: "convert-video",
        DisplayName: L.Get("ConversionBatchSession_Convert_Video_886b85ef"),
        RequiresSharedOptions: true,
        DefaultOptions: null,
        PickerTitle: L.Get("ConversionBatchSession_FrameShift_Convert_Video_5b59db29"),
        PickerDescription: L.Get("ConversionBatchSession_Select_a_target_container_and_a_prof_903f8cd7"),
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
        DisplayName: L.Get("ConversionBatchSession_Convert_Audio_1ee9e970"),
        RequiresSharedOptions: true,
        DefaultOptions: null,
        PickerTitle: L.Get("ConversionBatchSession_FrameShift_Convert_Audio_5533886d"),
        PickerDescription: L.Get("ConversionBatchSession_Select_an_output_format_and_a_profil_508930ba"),
        SupportedSourceFormatsText: AudioConversionCatalog.GetSupportedSourceFormatsText(),
        MutexName: @"Local\FrameShift_ConvertAudioBatch",
        PipeName: "FrameShift_ConvertAudioBatchQueue",
        ShowProfiles: true,
        IsSupportedSourceExtension: AudioConversionCatalog.IsSupportedSourceExtension,
        GetTargetsForSelection: _ => AudioConversionCatalog.GetTargets(),
        GetProfiles: AudioConversionCatalog.GetProfiles);

    public static BatchDefinition CreateImageDefinition() => new(
        ActionId: "convert-image",
        DisplayName: L.Get("ConversionBatchSession_Convert_Image_2c4e60c4"),
        RequiresSharedOptions: true,
        DefaultOptions: null,
        PickerTitle: L.Get("ConversionBatchSession_FrameShift_Convert_Image_8258f3d7"),
        PickerDescription: L.Get("ConversionBatchSession_Select_an_output_format_and_a_profil_508930ba"),
        SupportedSourceFormatsText: ImageConversionCatalog.GetSupportedSourceFormatsText(),
        MutexName: @"Local\FrameShift_ConvertImageBatch",
        PipeName: "FrameShift_ConvertImageBatchQueue",
        ShowProfiles: true,
        IsSupportedSourceExtension: ImageConversionCatalog.IsSupportedSourceExtension,
        GetTargetsForSelection: _ => ImageConversionCatalog.GetTargets(),
        GetProfiles: ImageConversionCatalog.GetProfiles);

    public static BatchDefinition CreateCompressVideoDefinition() => new(
        ActionId: "compress-video",
        DisplayName: L.Get("ConversionBatchSession_Compress_Video_5667dc11"),
        RequiresSharedOptions: false,
        DefaultOptions: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
        PickerTitle: null,
        PickerDescription: null,
        SupportedSourceFormatsText: VideoCompressionCatalog.GetSupportedSourceFormatsText(),
        MutexName: @"Local\FrameShift_CompressVideoBatch",
        PipeName: "FrameShift_CompressVideoBatchQueue",
        ShowProfiles: false,
        IsSupportedSourceExtension: VideoCompressionCatalog.IsSupportedSourceExtension,
        GetTargetsForSelection: _ => [],
        GetProfiles: static () => [],
        KeepWindowOpenOnFailure: true,
        PrimaryButtonText: L.Get("ConversionBatchSession_Compress_5830ffa3"));

    public static BatchDefinition CreateCompressAudioDefinition() => new(
        ActionId: "compress-audio",
        DisplayName: L.Get("ConversionBatchSession_Compress_Audio_7810ba2d"),
        RequiresSharedOptions: false,
        DefaultOptions: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
        PickerTitle: null,
        PickerDescription: null,
        SupportedSourceFormatsText: AudioConversionCatalog.GetSupportedSourceFormatsText(),
        MutexName: @"Local\FrameShift_CompressAudioBatch",
        PipeName: "FrameShift_CompressAudioBatchQueue",
        ShowProfiles: false,
        IsSupportedSourceExtension: AudioConversionCatalog.IsSupportedSourceExtension,
        GetTargetsForSelection: _ => [],
        GetProfiles: static () => [],
        KeepWindowOpenOnFailure: true,
        PrimaryButtonText: L.Get("ConversionBatchSession_Compress_5830ffa3"));

    public static BatchDefinition CreateCompressImageDefinition() => new(
        ActionId: "compress-image",
        DisplayName: L.Get("ConversionBatchSession_Compress_Image_6d46ce81"),
        RequiresSharedOptions: false,
        DefaultOptions: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
        PickerTitle: null,
        PickerDescription: null,
        SupportedSourceFormatsText: ImageConversionCatalog.GetSupportedSourceFormatsText(),
        MutexName: @"Local\FrameShift_CompressImageBatch",
        PipeName: "FrameShift_CompressImageBatchQueue",
        ShowProfiles: false,
        IsSupportedSourceExtension: ImageConversionCatalog.IsSupportedSourceExtension,
        GetTargetsForSelection: _ => [],
        GetProfiles: static () => [],
        KeepWindowOpenOnFailure: true,
        PrimaryButtonText: L.Get("ConversionBatchSession_Compress_5830ffa3"));

    public static BatchDefinition CreateExtractFramesDefinition() => new(
        ActionId: "extract-frames",
        DisplayName: L.Get("ConversionBatchSession_Extract_Frames_f68e8649"),
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
        PrimaryButtonText: L.Get("ConversionBatchSession_Extract_472c8d84"));

    public static BatchDefinition CreateExtractAudioDefinition() => new(
        ActionId: "extract-audio",
        DisplayName: L.Get("ConversionBatchSession_Extract_Audio_dfb515b5"),
        RequiresSharedOptions: true,
        DefaultOptions: null,
        PickerTitle: L.Get("ConversionBatchSession_FrameShift_Extract_Audio_3f130bf9"),
        PickerDescription: L.Get("ConversionBatchSession_Choose_the_output_audio_format_460b31d7"),
        SupportedSourceFormatsText: VideoConversionCatalog.GetSupportedSourceFormatsText(),
        MutexName: @"Local\FrameShift_ExtractAudioBatch",
        PipeName: "FrameShift_ExtractAudioBatchQueue",
        ShowProfiles: false,
        IsSupportedSourceExtension: VideoConversionCatalog.IsSupportedSourceExtension,
        GetTargetsForSelection: _ => ExtractAudioCatalog.GetTargets(),
        GetProfiles: static () => [],
        PrimaryButtonText: L.Get("ConversionBatchSession_Extract_472c8d84"));

    public static BatchDefinition CreateRemoveBackgroundDefinition() => new(
        ActionId: "remove-background",
        DisplayName: L.Get("ConversionBatchSession_Remove_Background_15a097fe"),
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
        PrimaryButtonText: L.Get("ConversionBatchSession_Process_4873226e"));

    public static BatchDefinition CreateUpscaleImageDefinition() => new(
        ActionId: "upscale-image",
        DisplayName: L.Get("ConversionBatchSession_Upscale_Image_a28d14ef"),
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
        PrimaryButtonText: L.Get("ConversionBatchSession_Upscale_17a396e6"));

    public static BatchDefinition CreateUpscaleVideoDefinition() => new(
        ActionId: "upscale-video",
        DisplayName: L.Get("ConversionBatchSession_Upscale_Video_a5fd36b8"),
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
        PrimaryButtonText: L.Get("ConversionBatchSession_Upscale_17a396e6"));

    public static BatchDefinition CreateCreateSubtitlesAudioDefinition() => new(
        ActionId: "create-subtitles-audio",
        DisplayName: L.Get("ConversionBatchSession_Create_Subtitle_File_1a0f5b50"),
        RequiresSharedOptions: false,
        DefaultOptions: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
        PickerTitle: null,
        PickerDescription: null,
        SupportedSourceFormatsText: FrameShift.Core.AI.CreateSubtitles.CreateSubtitlesAction.GetSupportedAudioExtensionsText(),
        MutexName: @"Local\FrameShift_CreateSubtitlesAudioBatch",
        PipeName: "FrameShift_CreateSubtitlesAudioBatchQueue",
        ShowProfiles: false,
        IsSupportedSourceExtension: FrameShift.Core.AI.CreateSubtitles.CreateSubtitlesAction.IsSupportedAudioExtension,
        GetTargetsForSelection: _ => [],
        GetProfiles: static () => [],
        KeepWindowOpenOnFailure: true,
        PrimaryButtonText: L.Get("ConversionBatchSession_Create_File_1f45a747"));

    public static BatchDefinition CreateCreateSubtitlesVideoDefinition() => new(
        ActionId: "create-subtitles-video",
        DisplayName: L.Get("ConversionBatchSession_Create_Subtitle_File_1a0f5b50"),
        RequiresSharedOptions: false,
        DefaultOptions: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
        PickerTitle: null,
        PickerDescription: null,
        SupportedSourceFormatsText: FrameShift.Core.AI.CreateSubtitles.CreateSubtitlesAction.GetSupportedVideoExtensionsText(),
        MutexName: @"Local\FrameShift_CreateSubtitlesVideoBatch",
        PipeName: "FrameShift_CreateSubtitlesVideoBatchQueue",
        ShowProfiles: false,
        IsSupportedSourceExtension: FrameShift.Core.AI.CreateSubtitles.CreateSubtitlesAction.IsSupportedVideoExtension,
        GetTargetsForSelection: _ => [],
        GetProfiles: static () => [],
        KeepWindowOpenOnFailure: true,
        PrimaryButtonText: L.Get("ConversionBatchSession_Create_File_1f45a747"));

    public static BatchDefinition CreateSeparateAudioDefinition() => new(
        ActionId: "separate-audio",
        DisplayName: L.Get("ConversionBatchSession_Audio_Separation_e0f92f56"),
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
        PrimaryButtonText: L.Get("ConversionBatchSession_Separate_a17d310d"));

    public static BatchDefinition CreateRemoveNoiseDefinition() => new(
        ActionId: "remove-noise",
        DisplayName: L.Get("ConversionBatchSession_Remove_Noise_1056e64f"),
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
        PrimaryButtonText: L.Get("ConversionBatchSession_Denoise_38e80a90"));

    public static BatchDefinition CreateRemoveNoiseVideoDefinition() => new(
        ActionId: "remove-noise-video",
        DisplayName: L.Get("ConversionBatchSession_Remove_Noise_Video_80c7b2b4"),
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
        PrimaryButtonText: L.Get("ConversionBatchSession_Denoise_38e80a90"));

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
                        if (TryBeginClosingForIdle())
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

                if (TryBeginClosingForIdle())
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
            _progressForm?.ReportState("failed", L.Format("ConversionBatchSession_Batch_0_failed_607787a7",_definition.DisplayName.ToLowerInvariant()));
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
        if (TryGetKnownItem(queueItem.QueueItemId, out var currentQueueItem))
        {
            queueItem = currentQueueItem;
        }

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

        if (_itemPreflight is not null)
        {
            ReportQueueItem(inputPath, "processing", L.Get("ConversionBatchSession_Checking_inputs_and_models_07e7990c"));
            BatchOptionResult prepared;
            try { prepared = _itemPreflight(ToQueueItemInfo(queueItem)); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                ExitCode = 1;
                _logger.Log($"Batch item preparation failed for '{inputPath}': {ex}");
                ReportQueueItem(inputPath, "failed", ex.Message);
                return;
            }
            if (!prepared.Success || prepared.Options is null)
            {
                ReportQueueItem(inputPath, "canceled", L.Get("ConversionBatchSession_Preparation_canceled_c2a07426"));
                return;
            }
            SetItemOptions(queueItem.QueueItemId, prepared.Options);
            if (TryGetKnownItem(queueItem.QueueItemId, out currentQueueItem)) queueItem = currentQueueItem;
            cancellationToken.ThrowIfCancellationRequested();
        }

        if (queueItem.Options is null && _lateItemOptionsProvider is not null)
        {
            var optionResult = PromptForLateItemOptions(ToQueueItemInfo(queueItem));
            if (!optionResult.Success || optionResult.Options is null)
            {
                ReportQueueItem(inputPath, "canceled", L.Get("ConversionBatchSession_Configuration_canceled_28a51321"));
                return;
            }

            SetItemOptions(queueItem.QueueItemId, optionResult.Options);
            if (TryGetKnownItem(queueItem.QueueItemId, out currentQueueItem))
            {
                queueItem = currentQueueItem;
            }
        }

        if (_sharedOptions is null)
        {
            ExitCode = 1;
            ReportQueueItem(inputPath, "failed", L.Get("ConversionBatchSession_Missing_action_options_5f5ab168"));
            return;
        }

        // Per-item options (carried over the pipe from the launch that queued this file) win
        // over the session's shared options, so each queued item runs with its own model.
        var effectiveOptions = MergeOptions(_sharedOptions, queueItem.Options);
        var request = new ActionRequest(inputPath, _logger, _progressForm, effectiveOptions);
        ReportQueueItem(inputPath, "processing", L.Get("ConversionBatchSession_Starting_1a300c49"));
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

    private bool TryGetKnownItem(string queueItemId, out BatchQueueItem queueItem)
    {
        lock (_sync)
        {
            if (_knownItems.TryGetValue(queueItemId, out var knownItem))
            {
                queueItem = knownItem;
                return true;
            }

            queueItem = null!;
            return false;
        }
    }

    private BatchOptionResult PromptForLateItemOptions(BatchQueueItemInfo item)
    {
        var provider = _lateItemOptionsProvider;
        if (provider is null)
        {
            return BatchOptionResult.Failed();
        }

        try
        {
            if (_progressForm is not null &&
                !_progressForm.IsDisposed &&
                _progressForm.IsHandleCreated &&
                _progressForm.InvokeRequired)
            {
                return (BatchOptionResult)_progressForm.Invoke(
                    new Func<BatchOptionResult>(() => provider(item)));
            }

            return provider(item);
        }
        catch (Exception ex)
        {
            _logger.Log($"ConversionBatchSession: late item options provider failed for {item.InputPath}: {ex}");
            return BatchOptionResult.Failed();
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
            : L.Format("ConversionBatchSession_0_selected_files_dd068a4b",supportedPaths.Length);

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

    private bool TryBeginClosingForIdle()
    {
        NamedPipeServerStream? pipeServer;
        lock (_sync)
        {
            var elapsed = DateTime.UtcNow - new DateTime(Interlocked.Read(ref _queueState.LastActivityTicks), DateTimeKind.Utc);
            if (_closing || !_pickerHandled || !_pendingPaths.IsEmpty || elapsed.TotalMilliseconds < IdleCloseMilliseconds)
            {
                return false;
            }

            _closing = true;
            pipeServer = _activePipeServer;
        }

        DisposePipeServer(pipeServer);
        return true;
    }

    private void StartPipeServer()
    {
        _logger.Log("ConversionBatchSession: batch listener started.");
        var serverThread = new Thread(() =>
        {
            _logger.Log("ConversionBatchSession: pipe listener thread entered.");
            while (!IsClosing() && !_sessionCancellationToken.IsCancellationRequested)
            {
                try
                {
                    using var pipeServer = LocalPipeTransport.CreateServer(_definition.PipeName, PipeDirection.InOut);

                    if (!TryRegisterPipeServer(pipeServer))
                    {
                        return;
                    }

                    pipeServer.WaitForConnectionAsync(_sessionCancellationToken).GetAwaiter().GetResult();
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_sessionCancellationToken);
                    deadline.CancelAfter(_pipeTimeout);
                    var line = LocalPipeTransport.ReadAsync(pipeServer, deadline.Token).GetAwaiter().GetResult();
                    IReadOnlyList<BatchQueueItem> acceptedItems = [];
                    var accepted = TryParseQueueInvocation(line, out var invocation) && TryAcceptInvocation(invocation, out acceptedItems);
                    if (accepted) ReportAcceptedInvocation(acceptedItems);
                    // Acceptance is committed under _sync; potentially blocked I/O is always outside.
                    LocalPipeTransport.WriteAsync(pipeServer, LocalPipeTransport.Serialize(new QueueInvocationResponse(accepted), s_queueMessageJsonOptions),
                        deadline.Token).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    if (!IsClosing())
                    {
                        _logger.Log($"Convert batch pipe server error ({_definition.ActionId}):{Environment.NewLine}{ex}");
                        if (!_sessionCancellationToken.IsCancellationRequested) Thread.Sleep(100);
                    }
                }
                finally
                {
                    ClearActivePipeServer();
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

    private static bool TryParseQueueInvocation(string? line, out QueueInvocationMessage invocation)
    {
        invocation = new QueueInvocationMessage(string.Empty, []);
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        if (line.TrimStart().StartsWith('{'))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<QueueInvocationMessage>(line, s_queueMessageJsonOptions);
                if (parsed is not null &&
                    !string.IsNullOrWhiteSpace(parsed.InvocationId) &&
                    parsed.Items is { Length: > 0 })
                {
                    invocation = parsed;
                    return true;
                }
            }
            catch (JsonException)
            {
                return false;
            }
        }

        if (!TryParseQueueMessage(line, out var inputPath, out var options))
        {
            return false;
        }

        invocation = new QueueInvocationMessage(
            Guid.NewGuid().ToString("N"),
            [new QueueMessage(inputPath, options is null ? null : new Dictionary<string, string>(options))]);
        return true;
    }

    private bool TryAcceptInvocation(QueueInvocationMessage invocation, out IReadOnlyList<BatchQueueItem> acceptedItems)
    {
        acceptedItems = [];
        if (string.IsNullOrWhiteSpace(invocation.InvocationId) || invocation.InvocationId.Length > 128 ||
            invocation.Items is not { Length: > 0 and <= LocalPipeTransport.MaximumPaths } ||
            invocation.Items.Any(item => item is null || string.IsNullOrWhiteSpace(item.Path))) return false;
        try { LocalPipeTransport.ValidatePaths(invocation.Items.Select(item => item.Path).ToArray()); }
        catch (InvalidDataException) { return false; }
        lock (_sync)
        {
            if (_closing || _globalCancelRequested || _sessionCancellationToken.IsCancellationRequested) return false;
            if (_acceptedInvocationIds.Contains(invocation.InvocationId)) return true;
            var items = invocation.Items.Select(item => CreateQueueItem(item.Path, item.Options)).ToArray();
            foreach (var item in items)
            {
                _knownItems[item.QueueItemId] = item;
                _removedItems.Remove(item.QueueItemId);
                _pendingPaths.Enqueue(item);
            }
            _acceptedInvocationIds.Add(invocation.InvocationId);
            Interlocked.Exchange(ref _queueState.LastActivityTicks, DateTime.UtcNow.Ticks);
            acceptedItems = items;
            return true;
        }
    }

    private void ReportAcceptedInvocation(IReadOnlyList<BatchQueueItem> acceptedItems)
    {
        foreach (var queueItem in acceptedItems)
        {
            _progressForm?.AddQueueItem(queueItem.QueueItemId, queueItem.InputPath);
            _logger.Log($"QUEUED FROM SECONDARY INSTANCE ({_definition.ActionId}): {queueItem.InputPath} [queueItemId={queueItem.QueueItemId}, options={DescribeOptions(queueItem.Options)}]");
        }
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
            if (_closing || _globalCancelRequested || _sessionCancellationToken.IsCancellationRequested)
            {
                return;
            }

            _knownItems[queueItem.QueueItemId] = queueItem;
            _removedItems.Remove(queueItem.QueueItemId);
            _pendingPaths.Enqueue(queueItem);
            Interlocked.Exchange(ref _queueState.LastActivityTicks, DateTime.UtcNow.Ticks);
        }

        _progressForm?.AddQueueItem(queueItem.QueueItemId, inputPath);
        _logger.Log($"QUEUED ({_definition.ActionId}): {inputPath} [queueItemId={queueItem.QueueItemId}, options={DescribeOptions(options)}]");
    }

    private bool TryRegisterPipeServer(NamedPipeServerStream pipeServer)
    {
        lock (_sync)
        {
            if (_closing)
            {
                return false;
            }

            _activePipeServer = pipeServer;
            return true;
        }
    }

    private void ClearActivePipeServer()
    {
        lock (_sync)
        {
            _activePipeServer = null;
        }
    }

    private bool IsClosing()
    {
        lock (_sync)
        {
            return _closing;
        }
    }

    private void BeginClosing()
    {
        NamedPipeServerStream? pipeServer;
        lock (_sync)
        {
            _closing = true;
            pipeServer = _activePipeServer;
        }

        DisposePipeServer(pipeServer);
    }

    private static void DisposePipeServer(NamedPipeServerStream? pipeServer)
    {
        try
        {
            pipeServer?.Dispose();
        }
        catch (ObjectDisposedException)
        {
            // The listener owns the stream and may have completed concurrently.
        }
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
        lock (_sync)
        {
            _globalCancelRequested = true;
        }
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
            return L.Get("ConversionBatchSession_selected_file_d65a205d");
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
        BeginClosing();
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
        BeginClosing();
        _progressForm?.EnableCloseMode(L.Get("ConversionBatchSession_Completed_with_errors_Review_the_que_52933d1e"));
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
        string PrimaryButtonText = "Convert")
    {
        public string MutexName { get; init; } = LocalPipeTransport.Name(MutexName);
        public string PipeName { get; init; } = LocalPipeTransport.Name(PipeName);
    }

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

    internal sealed record BatchQueueItemInfo(
        string QueueItemId,
        string InputPath,
        IReadOnlyDictionary<string, string>? Options);

    private static BatchQueueItemInfo ToQueueItemInfo(BatchQueueItem item) => new(
        item.QueueItemId,
        item.InputPath,
        item.Options);
}
