using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
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
    private readonly ConcurrentQueue<string> _pendingPaths = new();
    private readonly HashSet<string> _knownPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _removedPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();
    private readonly QueueSignalState _queueState = new();

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

        foreach (var inputPath in GetPendingPathsSnapshot())
        {
            _progressForm.AddQueueItem(inputPath);
        }
    }

    public static void SendPathsToPrimaryInstance(BatchDefinition definition, IEnumerable<string> inputPaths)
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
                writer.WriteLine(inputPath);
            }
        }
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

                if (_pendingPaths.TryDequeue(out var inputPath))
                {
                    if (IsRemoved(inputPath))
                    {
                        continue;
                    }

                    _currentInputPath = inputPath;
                    await ProcessQueuedPathAsync(inputPath, cancellationToken).ConfigureAwait(false);
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

    private async Task ProcessQueuedPathAsync(string inputPath, CancellationToken cancellationToken)
    {
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

        var request = new ActionRequest(inputPath, _logger, _progressForm, _sharedOptions);
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
            .Where(path => _definition.IsSupportedSourceExtension(Path.GetExtension(path)))
            .ToArray();

        if (supportedPaths.Length == 0)
        {
            ExitCode = 1;
            foreach (var pendingPath in pendingPaths)
            {
                ReportQueueItem(
                    pendingPath,
                    "failed",
                    MediaActionMessages.UnsupportedSourceFormat(
                        Path.GetExtension(pendingPath),
                        _definition.SupportedSourceFormatsText));
            }

            return BatchOptionResult.Failed();
        }

        var targets = _definition.GetTargetsForSelection(supportedPaths);
        if (targets.Count == 0)
        {
            ExitCode = 1;
            foreach (var supportedPath in supportedPaths)
            {
                ReportQueueItem(
                    supportedPath,
                    "failed",
                    MediaActionMessages.NoAlternativeTargetFormatsAvailable());
            }

            return BatchOptionResult.Failed();
        }

        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var sourceLabel = supportedPaths.Length == 1
            ? FormatSelectionLabel(Path.GetFileName(supportedPaths[0]))
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

    private string[] GetPendingPathsSnapshot()
    {
        lock (_sync)
        {
            return _knownPaths
                .Where(path => !_removedPaths.Contains(path))
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
                        if (!string.IsNullOrWhiteSpace(line))
                        {
                            EnqueuePaths([line]);
                            _logger.Log($"QUEUED FROM SECONDARY INSTANCE ({_definition.ActionId}): " + line);
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

    private void EnqueuePaths(IEnumerable<string> inputPaths)
    {
        foreach (var inputPath in inputPaths)
        {
            if (string.IsNullOrWhiteSpace(inputPath))
            {
                continue;
            }

            lock (_sync)
            {
                _removedPaths.Remove(inputPath);
                if (!_knownPaths.Add(inputPath))
                {
                    continue;
                }
            }

            _pendingPaths.Enqueue(inputPath);
            Interlocked.Exchange(ref _queueState.LastActivityTicks, DateTime.UtcNow.Ticks);
            _progressForm?.AddQueueItem(inputPath);
            _logger.Log($"QUEUED ({_definition.ActionId}): " + inputPath);
        }
    }

    private void OnQueueItemRemoveRequested(object? sender, string inputPath)
    {
        lock (_sync)
        {
            if (!_knownPaths.Remove(inputPath))
            {
                return;
            }

            _removedPaths.Add(inputPath);
        }

        _logger.Log($"REMOVED FROM QUEUE ({_definition.ActionId}): " + inputPath);
    }

    private void OnCancelRequested(object? sender, EventArgs e)
    {
        _globalCancelRequested = true;
        _logger.Log("ConversionBatchSession: OnCancelRequested entered.");
        var canceledBeforeProcessing = CancelPendingPaths();
        _logger.Log($"ConversionBatchSession: OnCancelRequested canceled pending items count={canceledBeforeProcessing.Length}.");
        foreach (var path in canceledBeforeProcessing)
        {
            ReportQueueItem(path, "canceled", MediaActionMessages.CanceledBeforeProcessing());
        }

        ScheduleGlobalCancelDiagnostics("cancel-requested");
    }

    private bool IsRemoved(string inputPath)
    {
        lock (_sync)
        {
            if (_removedPaths.Remove(inputPath))
            {
                return true;
            }
        }

        return false;
    }

    private string[] CancelPendingPaths()
    {
        lock (_sync)
        {
            var pendingPaths = _pendingPaths
                .ToArray()
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            while (_pendingPaths.TryDequeue(out _))
            {
            }

            foreach (var path in pendingPaths)
            {
                _removedPaths.Add(path);
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
}
