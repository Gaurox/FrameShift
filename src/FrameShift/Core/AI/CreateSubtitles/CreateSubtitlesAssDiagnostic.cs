using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FrameShift.Core.AI.CreateSubtitles;

internal static class CreateSubtitlesAssDiagnosticWriter
{
    private const double MinimumRefinementSeconds = 0.050d;
    private const double MaximumRefinementSeconds = 0.350d;

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = true
    };

    internal static CreateSubtitlesDisplayTimingAnalysis AnalyzeProject(
        SubtitleProject project,
        CreateSubtitlesWorkerResponse workerResponse,
        string normalizedAudioPath,
        Logging.AppLogger logger)
    {
        var tokens = (workerResponse.Tokens ?? []).OrderBy(static token => token.StartSeconds).ToArray();
        var cueAnalyses = new List<CreateSubtitlesDisplayTimingCueAnalysis>(project.Segments.Count);
        var audioAnalyzer = SimpleMonoWaveAudioOnsetAnalyzer.TryLoad(normalizedAudioPath, logger);

        for (var index = 0; index < project.Segments.Count; index++)
        {
            var segment = project.Segments[index];
            var rawCueStartSeconds = segment.Start.TotalSeconds;
            var cueEndSeconds = segment.End.TotalSeconds;
            var previousCueEndSeconds = index > 0
                ? project.Segments[index - 1].End.TotalSeconds
                : (double?)null;
            var silenceSeconds = previousCueEndSeconds.HasValue
                ? rawCueStartSeconds - previousCueEndSeconds.Value
                : (double?)null;
            var firstUsefulWordStartSeconds = segment.Words.Count > 0
                ? segment.Words[0].StartSeconds
                : (double?)null;
            var secondWordStartSeconds = segment.Words.Count > 1
                ? segment.Words[1].StartSeconds
                : (double?)null;
            var firstUsefulWordText = segment.Words.Count > 0
                ? segment.Words[0].Text
                : null;
            var headTokens = firstUsefulWordStartSeconds.HasValue
                ? CollectHeadTokens(tokens, previousCueEndSeconds, firstUsefulWordStartSeconds.Value)
                : [];
            var onsetEstimate = firstUsefulWordStartSeconds.HasValue
                ? audioAnalyzer?.Estimate(firstUsefulWordStartSeconds.Value)
                : null;
            var refinedCueStartSeconds = ComputeRefinedDisplayStartSeconds(firstUsefulWordStartSeconds, secondWordStartSeconds, onsetEstimate);

            cueAnalyses.Add(new CreateSubtitlesDisplayTimingCueAnalysis
            {
                CueIndex = segment.Index,
                CueText = segment.Text,
                RawCueStartSeconds = rawCueStartSeconds,
                RefinedCueStartSeconds = refinedCueStartSeconds,
                CueEndSeconds = cueEndSeconds,
                PreviousCueEndSeconds = previousCueEndSeconds,
                TheoreticalSilenceSeconds = silenceSeconds,
                HasReliableWordAlignment = segment.HasReliableWordAlignment,
                FirstUsefulWordText = firstUsefulWordText,
                FirstUsefulWordStartSeconds = firstUsefulWordStartSeconds,
                SecondWordStartSeconds = secondWordStartSeconds,
                FirstTokenStartSeconds = headTokens.Count > 0 ? headTokens[0].StartSeconds : null,
                RawCueStartToFirstUsefulWordDeltaSeconds = firstUsefulWordStartSeconds.HasValue
                    ? rawCueStartSeconds - firstUsefulWordStartSeconds.Value
                    : null,
                RefinedCueStartToFirstUsefulWordDeltaSeconds = firstUsefulWordStartSeconds.HasValue
                    ? refinedCueStartSeconds - firstUsefulWordStartSeconds.Value
                    : null,
                AppliedRefinementSeconds = firstUsefulWordStartSeconds.HasValue
                    ? Math.Max(0d, refinedCueStartSeconds - firstUsefulWordStartSeconds.Value)
                    : 0d,
                HasLeadingWhitespaceOnlyToken = HasLeadingToken(headTokens, firstUsefulWordStartSeconds, static token => token.IsWhitespaceOnly),
                HasLeadingPunctuationOnlyToken = HasLeadingToken(headTokens, firstUsefulWordStartSeconds, static token => token.IsPunctuationOnly),
                HeadTokens = headTokens,
                EstimatedAudioOnsetSeconds = onsetEstimate?.EstimatedOnsetSeconds,
                EstimatedAudioOnsetDeltaFromWhisperSeconds = onsetEstimate?.DeltaFromWhisperSeconds,
                AudioNoiseFloorRms = onsetEstimate?.NoiseFloorRms,
                AudioThresholdRms = onsetEstimate?.ThresholdRms,
                AudioAnalysisWindowStartSeconds = onsetEstimate?.AnalysisWindowStartSeconds,
                AudioAnalysisWindowEndSeconds = onsetEstimate?.AnalysisWindowEndSeconds,
                AudioAnalysisNote = onsetEstimate?.Note
            });
        }

        return new CreateSubtitlesDisplayTimingAnalysis
        {
            RequestedPreset = string.Empty,
            ProviderUsed = workerResponse.ProviderUsed,
            DetectedLanguage = workerResponse.DetectedLanguage,
            WorkerWordCount = workerResponse.Words.Count,
            WorkerTokenCount = workerResponse.Tokens?.Count ?? 0,
            Cues = cueAnalyses
        };
    }

    internal static SubtitleProject ApplyRefinedDisplayStarts(
        SubtitleProject project,
        CreateSubtitlesDisplayTimingAnalysis analysis)
    {
        var mappedSegments = project.Segments
            .Select(segment =>
            {
                var cueAnalysis = analysis.Cues.FirstOrDefault(cue => cue.CueIndex == segment.Index);
                if (cueAnalysis is null)
                {
                    return segment;
                }

                var refinedStart = cueAnalysis.RefinedCueStartSeconds;
                if (refinedStart <= segment.Start.TotalSeconds + 0.0005d)
                {
                    return segment with { RefinedDisplayStart = null };
                }

                return segment with
                {
                    RefinedDisplayStart = TimeSpan.FromSeconds(refinedStart)
                };
            })
            .ToArray();

        return project with { Segments = mappedSegments };
    }

    public static async Task<string?> WriteReportIfNeededAsync(
        CreateSubtitlesOutputFormat outputFormat,
        string inputPath,
        string outputPath,
        SubtitleProject refinedProject,
        CreateSubtitlesDisplayTimingAnalysis analysis,
        CreateSubtitlesAssPreset requestedPreset,
        Logging.AppLogger logger,
        CancellationToken cancellationToken)
    {
        if (outputFormat != CreateSubtitlesOutputFormat.AdvancedAss)
        {
            return null;
        }

        var reportPath = outputPath + ".diagnostic.json";
        var report = BuildReport(inputPath, outputPath, refinedProject, analysis, requestedPreset);
        var json = JsonSerializer.Serialize(report, s_jsonOptions);
        await File.WriteAllTextAsync(reportPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken).ConfigureAwait(false);
        logger.Log($"CreateSubtitlesAction: ASS diagnostic written to '{reportPath}'.");
        return reportPath;
    }

    private static CreateSubtitlesAssDiagnosticReport BuildReport(
        string inputPath,
        string outputPath,
        SubtitleProject refinedProject,
        CreateSubtitlesDisplayTimingAnalysis analysis,
        CreateSubtitlesAssPreset requestedPreset)
    {
        var cueDiagnostics = new List<CreateSubtitlesAssCueDiagnostic>(refinedProject.Segments.Count);
        for (var index = 0; index < refinedProject.Segments.Count; index++)
        {
            var segment = refinedProject.Segments[index];
            var cueAnalysis = analysis.Cues[index];
            var effectivePreset = CreateSubtitlesAssFormatter.ResolveEffectivePreset(segment, requestedPreset);
            var cueDiagnostic = new CreateSubtitlesAssCueDiagnostic
            {
                CueIndex = segment.Index,
                CueText = segment.Text,
                CueStartSeconds = cueAnalysis.RefinedCueStartSeconds,
                RawCueStartSeconds = cueAnalysis.RawCueStartSeconds,
                AppliedRefinementSeconds = cueAnalysis.AppliedRefinementSeconds,
                CueEndSeconds = cueAnalysis.CueEndSeconds,
                PreviousCueEndSeconds = cueAnalysis.PreviousCueEndSeconds,
                TheoreticalSilenceSeconds = cueAnalysis.TheoreticalSilenceSeconds,
                RequestedPreset = requestedPreset.GetDisplayName(),
                EffectivePreset = effectivePreset.GetDisplayName(),
                HasReliableWordAlignment = cueAnalysis.HasReliableWordAlignment,
                FirstUsefulWordText = cueAnalysis.FirstUsefulWordText,
                FirstUsefulWordStartSeconds = cueAnalysis.FirstUsefulWordStartSeconds,
                SecondWordStartSeconds = cueAnalysis.SecondWordStartSeconds,
                FirstTokenStartSeconds = cueAnalysis.FirstTokenStartSeconds,
                RawCueStartToFirstUsefulWordDeltaSeconds = cueAnalysis.RawCueStartToFirstUsefulWordDeltaSeconds,
                RefinedCueStartToFirstUsefulWordDeltaSeconds = cueAnalysis.RefinedCueStartToFirstUsefulWordDeltaSeconds,
                HasLeadingWhitespaceOnlyToken = cueAnalysis.HasLeadingWhitespaceOnlyToken,
                HasLeadingPunctuationOnlyToken = cueAnalysis.HasLeadingPunctuationOnlyToken,
                HeadTokens = cueAnalysis.HeadTokens.Select(static token => new CreateSubtitlesAssHeadTokenDiagnostic
                {
                    StartSeconds = token.StartSeconds,
                    RawText = token.RawText,
                    TrimmedText = token.TrimmedText,
                    StartsNewWord = token.StartsNewWord,
                    IsWhitespaceOnly = token.IsWhitespaceOnly,
                    IsPunctuationOnly = token.IsPunctuationOnly
                }).ToList(),
                EstimatedAudioOnsetSeconds = cueAnalysis.EstimatedAudioOnsetSeconds,
                EstimatedAudioOnsetDeltaFromWhisperSeconds = cueAnalysis.EstimatedAudioOnsetDeltaFromWhisperSeconds,
                AudioNoiseFloorRms = cueAnalysis.AudioNoiseFloorRms,
                AudioThresholdRms = cueAnalysis.AudioThresholdRms,
                AudioAnalysisWindowStartSeconds = cueAnalysis.AudioAnalysisWindowStartSeconds,
                AudioAnalysisWindowEndSeconds = cueAnalysis.AudioAnalysisWindowEndSeconds,
                AudioAnalysisNote = cueAnalysis.AudioAnalysisNote
            };

            cueDiagnostic.LikelyEarlyDisplayOrigin = ClassifyLikelyOrigin(cueDiagnostic);
            cueDiagnostics.Add(cueDiagnostic);
        }

        var observations = BuildObservations(cueDiagnostics);
        var totalWordCount = refinedProject.Segments.Sum(static segment => segment.Words.Count);
        if (totalWordCount != analysis.WorkerWordCount)
        {
            observations.Add($"Word count mismatch between SubtitleProject ({totalWordCount.ToString(CultureInfo.InvariantCulture)}) and worker response ({analysis.WorkerWordCount.ToString(CultureInfo.InvariantCulture)}).");
        }

        return new CreateSubtitlesAssDiagnosticReport
        {
            GeneratedUtc = DateTime.UtcNow,
            InputPath = inputPath,
            OutputAssPath = outputPath,
            RequestedPreset = requestedPreset.GetDisplayName(),
            ProviderUsed = analysis.ProviderUsed,
            DetectedLanguage = analysis.DetectedLanguage,
            CueCount = cueDiagnostics.Count,
            WorkerWordCount = analysis.WorkerWordCount,
            WorkerTokenCount = analysis.WorkerTokenCount,
            SummaryObservations = observations,
            Cues = cueDiagnostics
        };
    }

    private static double ComputeRefinedDisplayStartSeconds(
        double? firstUsefulWordStartSeconds,
        double? secondWordStartSeconds,
        CreateSubtitlesAudioOnsetEstimate? onsetEstimate)
    {
        if (!firstUsefulWordStartSeconds.HasValue)
        {
            return 0d;
        }

        var rawStartSeconds = firstUsefulWordStartSeconds.Value;
        var onsetSeconds = onsetEstimate?.EstimatedOnsetSeconds;
        if (!onsetSeconds.HasValue || onsetSeconds.Value <= rawStartSeconds)
        {
            return rawStartSeconds;
        }

        var rawCorrection = onsetSeconds.Value - rawStartSeconds;
        if (rawCorrection < MinimumRefinementSeconds)
        {
            return rawStartSeconds;
        }

        var refinedStartSeconds = rawStartSeconds + Math.Min(rawCorrection, MaximumRefinementSeconds);
        if (secondWordStartSeconds.HasValue && secondWordStartSeconds.Value > rawStartSeconds)
        {
            refinedStartSeconds = Math.Min(refinedStartSeconds, secondWordStartSeconds.Value);
        }

        if (refinedStartSeconds <= rawStartSeconds)
        {
            return rawStartSeconds;
        }

        return refinedStartSeconds - rawStartSeconds < MinimumRefinementSeconds
            ? rawStartSeconds
            : refinedStartSeconds;
    }

    private static List<CreateSubtitlesWorkerToken> CollectHeadTokens(
        IReadOnlyList<CreateSubtitlesWorkerToken> tokens,
        double? previousCueEndSeconds,
        double firstUsefulWordStartSeconds)
    {
        if (tokens.Count == 0)
        {
            return [];
        }

        var lowerBound = Math.Max(0d, (previousCueEndSeconds ?? firstUsefulWordStartSeconds) - 0.12d);
        var beforeTokens = tokens
            .Where(token => token.StartSeconds >= lowerBound && token.StartSeconds < firstUsefulWordStartSeconds - 0.001d)
            .TakeLast(3)
            .ToList();
        var afterTokens = tokens
            .Where(token => token.StartSeconds >= firstUsefulWordStartSeconds - 0.001d && token.StartSeconds <= firstUsefulWordStartSeconds + 0.20d)
            .Take(5)
            .ToList();

        if (beforeTokens.Count == 0 && afterTokens.Count == 0)
        {
            var fallback = tokens
                .Where(token => token.StartSeconds >= Math.Max(0d, firstUsefulWordStartSeconds - 0.05d))
                .Take(4)
                .ToList();
            return fallback;
        }

        return beforeTokens.Concat(afterTokens).ToList();
    }

    private static bool HasLeadingToken(
        IReadOnlyList<CreateSubtitlesWorkerToken> tokens,
        double? firstUsefulWordStartSeconds,
        Func<CreateSubtitlesWorkerToken, bool> predicate)
    {
        if (!firstUsefulWordStartSeconds.HasValue)
        {
            return false;
        }

        var threshold = firstUsefulWordStartSeconds.Value + 0.001d;
        return tokens.Any(token => token.StartSeconds <= threshold && predicate(token));
    }

    private static string ClassifyLikelyOrigin(CreateSubtitlesAssCueDiagnostic cue)
    {
        if (cue.RawCueStartToFirstUsefulWordDeltaSeconds is < -0.015d)
        {
            return "Cue starts before the first useful word timestamp.";
        }

        if (cue.EstimatedAudioOnsetDeltaFromWhisperSeconds is > 0.080d &&
            Math.Abs(cue.RawCueStartToFirstUsefulWordDeltaSeconds ?? 0d) <= 0.015d)
        {
            return "Whisper timestamp looks earlier than the estimated acoustic onset.";
        }

        if ((cue.HasLeadingWhitespaceOnlyToken || cue.HasLeadingPunctuationOnlyToken) &&
            Math.Abs(cue.RawCueStartToFirstUsefulWordDeltaSeconds ?? 0d) <= 0.015d)
        {
            return "Cue start matches the first useful word, but leading non-lexical tokens exist near the boundary.";
        }

        if (cue.EffectivePreset != CreateSubtitlesAssPreset.Classic.GetDisplayName() &&
            Math.Abs(cue.RawCueStartToFirstUsefulWordDeltaSeconds ?? 0d) <= 0.015d)
        {
            return "Dynamic ASS preset begins exactly at the first useful word timestamp; any early display likely comes from upstream timings.";
        }

        return "No obvious early-start signal detected from cue timing alone.";
    }

    private static List<string> BuildObservations(IReadOnlyList<CreateSubtitlesAssCueDiagnostic> cues)
    {
        var observations = new List<string>();
        if (cues.Count == 0)
        {
            observations.Add("No cue was exported.");
            return observations;
        }

        var rawCueStartsMatchWords = cues.Count(cue => Math.Abs(cue.RawCueStartToFirstUsefulWordDeltaSeconds ?? 0d) <= 0.015d);
        observations.Add($"{rawCueStartsMatchWords.ToString(CultureInfo.InvariantCulture)}/{cues.Count.ToString(CultureInfo.InvariantCulture)} raw cues start within 15 ms of their first useful word timestamp.");

        var refinedCueStartsMatchWords = cues.Count(cue => (cue.RefinedCueStartToFirstUsefulWordDeltaSeconds ?? 0d) >= -0.015d);
        observations.Add($"{refinedCueStartsMatchWords.ToString(CultureInfo.InvariantCulture)}/{cues.Count.ToString(CultureInfo.InvariantCulture)} refined cues do not start before their first useful word timestamp.");

        var correctedCues = cues.Count(cue => cue.AppliedRefinementSeconds > 0d);
        observations.Add($"{correctedCues.ToString(CultureInfo.InvariantCulture)} cues received a conservative delayed display start.");

        var likelyWhisperEarly = cues
            .Where(cue => cue.EstimatedAudioOnsetDeltaFromWhisperSeconds is > 0.080d)
            .Select(static cue => cue.CueIndex)
            .ToArray();
        if (likelyWhisperEarly.Length > 0)
        {
            observations.Add($"Estimated acoustic onset happens more than 80 ms after Whisper on cues: {string.Join(", ", likelyWhisperEarly)}.");
        }

        var possibleSilenceDisplay = cues
            .Where(cue => cue.TheoreticalSilenceSeconds is > 0.120d &&
                          cue.EstimatedAudioOnsetDeltaFromWhisperSeconds is > 0.080d &&
                          Math.Abs(cue.RawCueStartToFirstUsefulWordDeltaSeconds ?? 0d) <= 0.015d)
            .Select(static cue => cue.CueIndex)
            .ToArray();
        if (possibleSilenceDisplay.Length > 0)
        {
            observations.Add($"Cues likely visible during a silence because Whisper starts early: {string.Join(", ", possibleSilenceDisplay)}.");
        }

        var largeCorrections = cues
            .Where(cue => cue.AppliedRefinementSeconds >= MinimumRefinementSeconds)
            .Select(static cue => $"{cue.CueIndex} (+{cue.AppliedRefinementSeconds.ToString("0.###", CultureInfo.InvariantCulture)} s)")
            .ToArray();
        if (largeCorrections.Length > 0)
        {
            observations.Add($"Applied delayed display start on cues: {string.Join(", ", largeCorrections)}.");
        }

        var leadingNonLexical = cues
            .Where(cue => cue.HasLeadingWhitespaceOnlyToken || cue.HasLeadingPunctuationOnlyToken)
            .Select(static cue => cue.CueIndex)
            .ToArray();
        if (leadingNonLexical.Length > 0)
        {
            observations.Add($"Leading whitespace or punctuation tokens were detected near the cue boundary on cues: {string.Join(", ", leadingNonLexical)}.");
        }

        return observations;
    }

    private sealed class SimpleMonoWaveAudioOnsetAnalyzer
    {
        private const int ExpectedSampleRate = 16_000;
        private const double BaselinePreRollSeconds = 0.25d;
        private const double SearchPreRollSeconds = 0.08d;
        private const double SearchPostRollSeconds = 0.40d;
        private const double BaselineSafetyGapSeconds = 0.03d;
        private const int FrameSamples = 160;
        private const int HopSamples = 80;

        private readonly float[] _samples;
        private readonly int _sampleRate;

        private SimpleMonoWaveAudioOnsetAnalyzer(float[] samples, int sampleRate)
        {
            _samples = samples;
            _sampleRate = sampleRate;
        }

        public static SimpleMonoWaveAudioOnsetAnalyzer? TryLoad(string path, Logging.AppLogger logger)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return null;
                }

                var (sampleRate, channels, bitsPerSample, samples) = LoadMonoPcm16(path);
                if (channels != 1 || bitsPerSample != 16 || sampleRate != ExpectedSampleRate || samples.Length == 0)
                {
                    logger.Log($"CreateSubtitlesAction: audio onset analyzer skipped. sampleRate={sampleRate}, channels={channels}, bitsPerSample={bitsPerSample}, sampleCount={samples.Length}.");
                    return null;
                }

                return new SimpleMonoWaveAudioOnsetAnalyzer(samples, sampleRate);
            }
            catch (Exception ex)
            {
                logger.Log($"CreateSubtitlesAction: audio onset analyzer load failed. {ex.Message}");
                return null;
            }
        }

        public CreateSubtitlesAudioOnsetEstimate Estimate(double whisperStartSeconds)
        {
            if (_samples.Length < FrameSamples)
            {
                return new CreateSubtitlesAudioOnsetEstimate
                {
                    AnalysisWindowStartSeconds = Math.Max(0d, whisperStartSeconds - SearchPreRollSeconds),
                    AnalysisWindowEndSeconds = whisperStartSeconds + SearchPostRollSeconds,
                    Note = "Normalized audio is too short for onset analysis."
                };
            }

            var analysisStartSeconds = Math.Max(0d, whisperStartSeconds - SearchPreRollSeconds);
            var analysisEndSeconds = Math.Min(_samples.Length / (double)_sampleRate, whisperStartSeconds + SearchPostRollSeconds);
            var baselineStartSeconds = Math.Max(0d, whisperStartSeconds - BaselinePreRollSeconds);
            var baselineEndSeconds = Math.Max(baselineStartSeconds, whisperStartSeconds - BaselineSafetyGapSeconds);

            var frames = BuildFrames(analysisStartSeconds, analysisEndSeconds);
            if (frames.Count == 0)
            {
                return new CreateSubtitlesAudioOnsetEstimate
                {
                    AnalysisWindowStartSeconds = analysisStartSeconds,
                    AnalysisWindowEndSeconds = analysisEndSeconds,
                    Note = "No RMS frame could be built in the analysis window."
                };
            }

            var baselineFrames = frames
                .Where(frame => frame.CenterSeconds >= baselineStartSeconds && frame.CenterSeconds <= baselineEndSeconds)
                .Select(static frame => frame.Rms)
                .ToArray();
            var noiseFloor = baselineFrames.Length > 0
                ? Median(baselineFrames)
                : frames.Min(static frame => frame.Rms);
            var threshold = Math.Max(0.010d, Math.Max(noiseFloor * 3.5d, noiseFloor + 0.008d));

            double? onsetSeconds = null;
            var consecutive = 0;
            for (var index = 0; index < frames.Count; index++)
            {
                if (frames[index].Rms >= threshold)
                {
                    consecutive++;
                    if (consecutive >= 2)
                    {
                        onsetSeconds = frames[Math.Max(0, index - 1)].CenterSeconds;
                        break;
                    }
                }
                else
                {
                    consecutive = 0;
                }
            }

            onsetSeconds ??= frames.FirstOrDefault(frame => frame.Rms >= threshold)?.CenterSeconds;
            return new CreateSubtitlesAudioOnsetEstimate
            {
                EstimatedOnsetSeconds = onsetSeconds,
                DeltaFromWhisperSeconds = onsetSeconds.HasValue ? onsetSeconds.Value - whisperStartSeconds : null,
                NoiseFloorRms = noiseFloor,
                ThresholdRms = threshold,
                AnalysisWindowStartSeconds = analysisStartSeconds,
                AnalysisWindowEndSeconds = analysisEndSeconds,
                Note = onsetSeconds.HasValue
                    ? "Estimated from 10 ms RMS frames with 5 ms hop on the normalized mono 16 kHz WAV."
                    : "No frame crossed the RMS threshold in the analysis window."
            };
        }

        private List<AudioFrame> BuildFrames(double startSeconds, double endSeconds)
        {
            var frames = new List<AudioFrame>();
            var startSample = Math.Max(0, (int)Math.Floor(startSeconds * _sampleRate));
            var endSample = Math.Min(_samples.Length, (int)Math.Ceiling(endSeconds * _sampleRate));
            for (var sampleIndex = startSample; sampleIndex + FrameSamples <= endSample; sampleIndex += HopSamples)
            {
                double sumSquares = 0d;
                for (var offset = 0; offset < FrameSamples; offset++)
                {
                    var sample = _samples[sampleIndex + offset];
                    sumSquares += sample * sample;
                }

                var rms = Math.Sqrt(sumSquares / FrameSamples);
                var centerSeconds = (sampleIndex + (FrameSamples / 2d)) / _sampleRate;
                frames.Add(new AudioFrame(centerSeconds, rms));
            }

            return frames;
        }

        private static (int SampleRate, int Channels, int BitsPerSample, float[] Samples) LoadMonoPcm16(string path)
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);
            if (new string(reader.ReadChars(4)) != "RIFF")
            {
                throw new InvalidDataException("Invalid WAV header: missing RIFF.");
            }

            _ = reader.ReadInt32();
            if (new string(reader.ReadChars(4)) != "WAVE")
            {
                throw new InvalidDataException("Invalid WAV header: missing WAVE.");
            }

            short audioFormat = 0;
            short channels = 0;
            int sampleRate = 0;
            short bitsPerSample = 0;
            byte[]? data = null;

            while (stream.Position + 8 <= stream.Length)
            {
                var chunkId = new string(reader.ReadChars(4));
                var chunkSize = reader.ReadInt32();
                if (chunkSize < 0)
                {
                    throw new InvalidDataException("Invalid WAV chunk size.");
                }

                switch (chunkId)
                {
                    case "fmt ":
                        audioFormat = reader.ReadInt16();
                        channels = reader.ReadInt16();
                        sampleRate = reader.ReadInt32();
                        _ = reader.ReadInt32();
                        _ = reader.ReadInt16();
                        bitsPerSample = reader.ReadInt16();
                        var remainingFmtBytes = chunkSize - 16;
                        if (remainingFmtBytes > 0)
                        {
                            reader.ReadBytes(remainingFmtBytes);
                        }

                        break;

                    case "data":
                        data = reader.ReadBytes(chunkSize);
                        break;

                    default:
                        reader.ReadBytes(chunkSize);
                        break;
                }

                if ((chunkSize & 1) != 0 && stream.Position < stream.Length)
                {
                    stream.Position += 1;
                }

                if (data is not null && sampleRate > 0 && channels > 0 && bitsPerSample > 0)
                {
                    break;
                }
            }

            if (audioFormat != 1 || data is null)
            {
                throw new InvalidDataException("Only PCM WAV with a data chunk is supported for onset analysis.");
            }

            var sampleCount = data.Length / 2;
            var samples = new float[sampleCount];
            for (var index = 0; index < sampleCount; index++)
            {
                var pcm = BitConverter.ToInt16(data, index * 2);
                samples[index] = pcm / 32768f;
            }

            return (sampleRate, channels, bitsPerSample, samples);
        }

        private static double Median(double[] values)
        {
            if (values.Length == 0)
            {
                return 0d;
            }

            Array.Sort(values);
            var midpoint = values.Length / 2;
            return (values.Length & 1) == 1
                ? values[midpoint]
                : (values[midpoint - 1] + values[midpoint]) / 2d;
        }

        private sealed record AudioFrame(double CenterSeconds, double Rms);
    }
}

internal sealed class CreateSubtitlesAssDiagnosticReport
{
    public DateTime GeneratedUtc { get; set; }
    public string InputPath { get; set; } = string.Empty;
    public string OutputAssPath { get; set; } = string.Empty;
    public string RequestedPreset { get; set; } = string.Empty;
    public string? ProviderUsed { get; set; }
    public string? DetectedLanguage { get; set; }
    public int CueCount { get; set; }
    public int WorkerWordCount { get; set; }
    public int WorkerTokenCount { get; set; }
    public List<string> SummaryObservations { get; set; } = [];
    public List<CreateSubtitlesAssCueDiagnostic> Cues { get; set; } = [];
}

internal sealed class CreateSubtitlesAssCueDiagnostic
{
    public int CueIndex { get; set; }
    public string CueText { get; set; } = string.Empty;
    public double CueStartSeconds { get; set; }
    public double RawCueStartSeconds { get; set; }
    public double AppliedRefinementSeconds { get; set; }
    public double CueEndSeconds { get; set; }
    public double? PreviousCueEndSeconds { get; set; }
    public double? TheoreticalSilenceSeconds { get; set; }
    public string RequestedPreset { get; set; } = string.Empty;
    public string EffectivePreset { get; set; } = string.Empty;
    public bool HasReliableWordAlignment { get; set; }
    public string? FirstUsefulWordText { get; set; }
    public double? FirstUsefulWordStartSeconds { get; set; }
    public double? SecondWordStartSeconds { get; set; }
    public double? FirstTokenStartSeconds { get; set; }
    public double? RawCueStartToFirstUsefulWordDeltaSeconds { get; set; }
    public double? RefinedCueStartToFirstUsefulWordDeltaSeconds { get; set; }
    public bool HasLeadingWhitespaceOnlyToken { get; set; }
    public bool HasLeadingPunctuationOnlyToken { get; set; }
    public List<CreateSubtitlesAssHeadTokenDiagnostic> HeadTokens { get; set; } = [];
    public double? EstimatedAudioOnsetSeconds { get; set; }
    public double? EstimatedAudioOnsetDeltaFromWhisperSeconds { get; set; }
    public double? AudioNoiseFloorRms { get; set; }
    public double? AudioThresholdRms { get; set; }
    public double? AudioAnalysisWindowStartSeconds { get; set; }
    public double? AudioAnalysisWindowEndSeconds { get; set; }
    public string? AudioAnalysisNote { get; set; }
    public string LikelyEarlyDisplayOrigin { get; set; } = string.Empty;
}

internal sealed class CreateSubtitlesDisplayTimingAnalysis
{
    public string RequestedPreset { get; set; } = string.Empty;
    public string? ProviderUsed { get; set; }
    public string? DetectedLanguage { get; set; }
    public int WorkerWordCount { get; set; }
    public int WorkerTokenCount { get; set; }
    public List<CreateSubtitlesDisplayTimingCueAnalysis> Cues { get; set; } = [];
}

internal sealed class CreateSubtitlesDisplayTimingCueAnalysis
{
    public int CueIndex { get; set; }
    public string CueText { get; set; } = string.Empty;
    public double RawCueStartSeconds { get; set; }
    public double RefinedCueStartSeconds { get; set; }
    public double CueEndSeconds { get; set; }
    public double? PreviousCueEndSeconds { get; set; }
    public double? TheoreticalSilenceSeconds { get; set; }
    public bool HasReliableWordAlignment { get; set; }
    public string? FirstUsefulWordText { get; set; }
    public double? FirstUsefulWordStartSeconds { get; set; }
    public double? SecondWordStartSeconds { get; set; }
    public double? FirstTokenStartSeconds { get; set; }
    public double? RawCueStartToFirstUsefulWordDeltaSeconds { get; set; }
    public double? RefinedCueStartToFirstUsefulWordDeltaSeconds { get; set; }
    public double AppliedRefinementSeconds { get; set; }
    public bool HasLeadingWhitespaceOnlyToken { get; set; }
    public bool HasLeadingPunctuationOnlyToken { get; set; }
    public List<CreateSubtitlesWorkerToken> HeadTokens { get; set; } = [];
    public double? EstimatedAudioOnsetSeconds { get; set; }
    public double? EstimatedAudioOnsetDeltaFromWhisperSeconds { get; set; }
    public double? AudioNoiseFloorRms { get; set; }
    public double? AudioThresholdRms { get; set; }
    public double? AudioAnalysisWindowStartSeconds { get; set; }
    public double? AudioAnalysisWindowEndSeconds { get; set; }
    public string? AudioAnalysisNote { get; set; }
}

internal sealed class CreateSubtitlesAssHeadTokenDiagnostic
{
    public double StartSeconds { get; set; }
    public string RawText { get; set; } = string.Empty;
    public string TrimmedText { get; set; } = string.Empty;
    public bool StartsNewWord { get; set; }
    public bool IsWhitespaceOnly { get; set; }
    public bool IsPunctuationOnly { get; set; }
}

internal sealed class CreateSubtitlesAudioOnsetEstimate
{
    public double? EstimatedOnsetSeconds { get; set; }
    public double? DeltaFromWhisperSeconds { get; set; }
    public double? NoiseFloorRms { get; set; }
    public double? ThresholdRms { get; set; }
    public double? AnalysisWindowStartSeconds { get; set; }
    public double? AnalysisWindowEndSeconds { get; set; }
    public string? Note { get; set; }
}
