using System.Globalization;

namespace FrameShift.Core.Actions;

public enum VideoLoopMode { Natural, PingPong }
public enum LoopSensitivity { Strict, Normal, Permissive }

public sealed record VideoLoopSettings(VideoLoopMode Mode, double Speed = 1, int Repetitions = 1, bool KeepAudio = true)
{
    public const string ModeKey = "loop-mode";
    public static readonly string[] OptionNames = [ModeKey, "loop-speed", "loop-repeat", "loop-audio"];

    public void Validate()
    {
        if (!Enum.IsDefined(Mode) || Speed is not (1 or 0.75 or 0.5) || Repetitions is < 1 or > 10)
            throw new ArgumentException("Loop speed must be 1, 0.75 or 0.5; repetitions must be between 1 and 10.");
        if (Mode == VideoLoopMode.Natural && Speed != 1)
            throw new ArgumentException("Slow motion is available only for Ping-Pong.");
        if (Mode == VideoLoopMode.PingPong && KeepAudio)
            throw new ArgumentException("Ping-Pong loops are silent. Reversed audio is not supported.");
    }

    public Dictionary<string, string> ToOptions() => new()
    {
        [ModeKey] = Mode == VideoLoopMode.Natural ? "natural" : "pingpong",
        ["loop-speed"] = Speed.ToString(CultureInfo.InvariantCulture),
        ["loop-repeat"] = Repetitions.ToString(CultureInfo.InvariantCulture),
        ["loop-audio"] = KeepAudio ? "keep" : "mute"
    };

    public static VideoLoopSettings? FromOptions(IReadOnlyDictionary<string, string>? options)
    {
        if (options is null || !options.TryGetValue(ModeKey, out var mode))
        {
            if (options?.Keys.Any(OptionNames.Contains) == true)
                throw new ArgumentException("Loop options require --loop-mode natural|pingpong.");
            return null;
        }
        var parsedMode = mode.ToLowerInvariant() switch
        {
            "natural" => VideoLoopMode.Natural,
            "pingpong" => VideoLoopMode.PingPong,
            _ => throw new ArgumentException("Loop mode must be natural or pingpong.")
        };
        var speed = options.TryGetValue("loop-speed", out var s) ? double.Parse(s, CultureInfo.InvariantCulture) : 1;
        var repeat = options.TryGetValue("loop-repeat", out var r) ? int.Parse(r, CultureInfo.InvariantCulture) : 1;
        var audio = options.TryGetValue("loop-audio", out var a) ? a.ToLowerInvariant() switch
        {
            "keep" => true, "mute" => false,
            _ => throw new ArgumentException("Loop audio must be keep or mute.")
        } : parsedMode == VideoLoopMode.Natural;
        var settings = new VideoLoopSettings(parsedMode, speed, repeat, audio);
        settings.Validate();
        return settings;
    }
}

public sealed record VideoLoopCandidate(double StartSeconds, double EndSeconds, double Quality)
{
    // Quality is a ranking score, not a probability or percentage of visual correctness.
    public VideoLoopSeamMetrics? Seam { get; init; }
    public double DurationSeconds => EndSeconds - StartSeconds;
    public string JunctionAssessment => Seam is null ? "Needs review" : Seam.Error <= 0.012 ? "Promising"
        : Seam.Error <= 0.04 ? "Approximate" : "Needs review";
    public override string ToString() => $"{CutVideoMath.FormatPreciseTime(StartSeconds)} → {CutVideoMath.FormatPreciseTime(EndSeconds)}  |  {DurationSeconds:0.###} s  |  {JunctionAssessment}";
}

public sealed record VideoLoopSeamMetrics(double Appearance, double Lighting, double Motion, double Continuity,
    double Scene, bool IsStatic, double ContextSeconds, int ComparedFrames)
{
    public double Error => Math.Max(Appearance, Scene) + Lighting * 0.25 + Motion * 0.5 + Continuity * 0.75;
}

[Flags]
public enum VideoLoopSearchLimits { None = 0, Sampling = 1, SeedSelection = 2, CandidateList = 4, TemporalCoverage = 8 }

public sealed record VideoLoopAnalysisDiagnostics
{
    public int SampledFrames { get; init; }
    public int RefinedFrames { get; init; }
    public int ExaminedSeeds { get; init; }
    public int AcceptedCandidates { get; init; }
    public long PeakRawTemporaryBytes { get; init; }
    public TimeSpan SamplingElapsed { get; init; }
    public TimeSpan SeedSelectionElapsed { get; init; }
    public TimeSpan RefinementElapsed { get; init; }
    public int SamplingBlocks { get; init; }
    public int MaximumResidentSampleFrames { get; init; }
    public long RetainedSignatureBytes { get; init; }
    public long ComparedSamplePairs { get; init; }
    public int TemporalRegions { get; init; }
    public double CoveredSeconds { get; init; }
    public bool FullTemporalCoverage { get; init; }
    public TimeSpan EndpointElapsed { get; init; }
    public long TimingFrames { get; init; }
    public int RefinementWidth { get; init; }
    public int RefinementHeight { get; init; }
    public int MaximumResidentRefinementFrames { get; init; }
}

// Completion of this bounded heuristic search never proves that every loop was found.
// Cancellation retains the existing OperationCanceledException contract.
public sealed record VideoLoopAnalysis(IReadOnlyList<VideoLoopCandidate> Candidates, double SampleRate)
{
    public bool SearchCompleted { get; init; } = true;
    public VideoLoopSearchLimits Limits { get; init; }
    public VideoLoopAnalysisDiagnostics Diagnostics { get; init; } = new();
}

public sealed record VideoLoopExportSelection(string CandidateId, VideoLoopCandidate Bounds, VideoLoopSettings Settings)
{
    internal VideoLoopSourceStamp? SourceStamp { get; init; }
    public static VideoLoopExportSelection FromCandidate(VideoLoopCandidate bounds, VideoLoopSettings settings)
        => new(bounds.StartSeconds.ToString("R", CultureInfo.InvariantCulture) + ":" +
            bounds.EndSeconds.ToString("R", CultureInfo.InvariantCulture), bounds, settings);

    public Dictionary<string, string> ToOptions()
    {
        if (string.IsNullOrWhiteSpace(CandidateId)) throw new ArgumentException("A loop candidate ID is required.");
        if (!double.IsFinite(Bounds.StartSeconds) || !double.IsFinite(Bounds.EndSeconds) ||
            Bounds.StartSeconds < 0 || Bounds.EndSeconds <= Bounds.StartSeconds)
            throw new ArgumentException("Loop boundaries must describe a finite, non-empty segment.");
        Settings.Validate();
        var options = Settings.ToOptions();
        options["loop-start"] = Bounds.StartSeconds.ToString("R", CultureInfo.InvariantCulture);
        options["loop-end"] = Bounds.EndSeconds.ToString("R", CultureInfo.InvariantCulture);
        options["loop-candidate"] = CandidateId;
        return options;
    }
}

public sealed record VideoLoopProgress(int Percent, string Message)
{
    public VideoLoopAnalysis? Results { get; init; }
}
