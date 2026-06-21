using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using SherpaOnnx;

namespace FrameShift.SubtitlesWorker;

internal static class Program
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = true
    };

    private static async Task<int> Main(string[] args)
    {
        if (!TryParseRequestPath(args, out var requestPath))
        {
            await Console.Error.WriteLineAsync("Missing required argument: --request <path>").ConfigureAwait(false);
            return 1;
        }

        var request = await LoadRequestAsync(requestPath).ConfigureAwait(false);
        var response = new CreateSubtitlesWorkerResponse();

        try
        {
            var wave = WaveLoader.LoadMono16Khz(request.WavPath);
            response.AudioDurationSeconds = wave.DurationSeconds;

            if (IsCancellationRequested(request.CancelSignalPath))
            {
                response.Canceled = true;
                response.Success = false;
                await SaveResponseAsync(request.ResponsePath, response).ConfigureAwait(false);
                return 0;
            }

            EmitProgress(2, "Loading Whisper language detector...");
            response.DetectedLanguage = DetectLanguage(request, wave);

            EmitProgress(18, "Loading Whisper recognizer (DirectML)...");
            var (recognizer, providerUsed) = CreateRecognizer(request);
            response.ProviderUsed = providerUsed;

            List<CreateSubtitlesWorkerWord> words = [];
            try
            {
                using (recognizer)
                {
                    EmitProgress(20, $"Whisper recognizer ready ({(providerUsed == "directml" ? "DirectML" : "CPU")}).");
                    words = TranscribeAllWindows(request, recognizer, wave);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception dmlEx) when (providerUsed == "directml")
            {
                // DirectML inference failed after successful init — restart from scratch on CPU
                Console.Error.WriteLine($"[SubtitlesWorker] DirectML inference failed, retrying with CPU: {dmlEx.Message}");
                EmitProgress(18, "Retrying with Whisper recognizer (CPU)...");
                using var cpuRecognizer = BuildRecognizer(request, "cpu");
                EmitProgress(20, "Whisper recognizer ready (CPU).");
                response.ProviderUsed = "cpu";
                words = TranscribeAllWindows(request, cpuRecognizer, wave);
            }

            response.Words = words;

            if (IsCancellationRequested(request.CancelSignalPath))
            {
                response.Canceled = true;
                response.Success = false;
            }
            else
            {
                response.Success = true;
            }

            EmitProgress(100, "Whisper transcription finished.");
            await SaveResponseAsync(request.ResponsePath, response).ConfigureAwait(false);
            return 0;
        }
        catch (OperationCanceledException)
        {
            response.Canceled = true;
            response.Success = false;
            await SaveResponseAsync(request.ResponsePath, response).ConfigureAwait(false);
            return 0;
        }
        catch (Exception ex)
        {
            response.Success = false;
            response.Canceled = false;
            response.ErrorMessage = ex.Message;
            await SaveResponseAsync(request.ResponsePath, response).ConfigureAwait(false);
            await Console.Error.WriteLineAsync(ex.ToString()).ConfigureAwait(false);
            return 1;
        }
    }

    private static string DetectLanguage(CreateSubtitlesWorkerRequest request, LoadedWave wave)
    {
        var config = new SpokenLanguageIdentificationConfig
        {
            Provider = "cpu",
            NumThreads = request.NumThreads,
            Debug = 0,
            Whisper = new SpokenLanguageIdentificationWhisperConfig
            {
                Encoder = request.EncoderPath,
                Decoder = request.DecoderPath,
                TailPaddings = 0
            }
        };

        using var lid = new SpokenLanguageIdentification(config);
        using var stream = lid.CreateStream();
        var detectionWindow = WaveWindowPlanner.TakeLanguageWindow(wave.Samples, wave.SampleRate);
        stream.AcceptWaveform(wave.SampleRate, detectionWindow);
        EmitProgress(10, "Detecting language...");
        var result = lid.Compute(stream);
        return result.Lang ?? string.Empty;
    }

    private static (OfflineRecognizer Recognizer, string ProviderUsed) CreateRecognizer(CreateSubtitlesWorkerRequest request)
    {
        try
        {
            return (BuildRecognizer(request, "directml"), "directml");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[SubtitlesWorker] DirectML init failed, falling back to CPU: {ex.Message}");
            return (BuildRecognizer(request, "cpu"), "cpu");
        }
    }

    private static OfflineRecognizer BuildRecognizer(CreateSubtitlesWorkerRequest request, string provider) =>
        new(new OfflineRecognizerConfig
        {
            FeatConfig = new FeatureConfig
            {
                SampleRate = 16000,
                FeatureDim = request.FeatureDim
            },
            ModelConfig = new OfflineModelConfig
            {
                Tokens = request.TokensPath,
                NumThreads = request.NumThreads,
                Debug = 0,
                Provider = provider,
                Whisper = new OfflineWhisperModelConfig
                {
                    Encoder = request.EncoderPath,
                    Decoder = request.DecoderPath,
                    Language = string.Empty,
                    Task = "transcribe",
                    TailPaddings = 0,
                    EnableTokenTimestamps = 1,
                    EnableSegmentTimestamps = 1
                }
            },
            DecodingMethod = "greedy_search",
            MaxActivePaths = 4
        });

    private static List<CreateSubtitlesWorkerWord> TranscribeAllWindows(
        CreateSubtitlesWorkerRequest request,
        OfflineRecognizer recognizer,
        LoadedWave wave)
    {
        var windows = WaveWindowPlanner.Plan(
            wave.Samples.Length,
            wave.SampleRate,
            request.WindowMilliseconds,
            request.OverlapMilliseconds);
        var mergedWords = new List<WordTiming>();

        for (var index = 0; index < windows.Count; index++)
        {
            if (IsCancellationRequested(request.CancelSignalPath))
            {
                throw new OperationCanceledException("Cancellation signal detected before the next Whisper window.");
            }

            var window = windows[index];
            EmitProgress(
                20 + (int)Math.Round((index / (double)Math.Max(1, windows.Count)) * 72d, MidpointRounding.AwayFromZero),
                $"Transcribing window {index + 1}/{windows.Count}...");

            var buffer = new float[window.LengthSamples];
            Array.Copy(wave.Samples, window.StartSample, buffer, 0, window.LengthSamples);

            using var stream = recognizer.CreateStream();
            stream.AcceptWaveform(wave.SampleRate, buffer);
            recognizer.Decode(stream);

            var result = stream.Result;
            var localWords = WhisperWordBuilder.BuildWords(
                result.Tokens ?? Array.Empty<string>(),
                result.Timestamps ?? Array.Empty<float>(),
                window.StartSeconds);

            WordMerger.Merge(mergedWords, localWords);
        }

        EmitProgress(96, "Finalizing merged timestamps...");
        return mergedWords
            .Select(word => new CreateSubtitlesWorkerWord
            {
                Text = word.Text,
                StartSeconds = word.StartSeconds
            })
            .ToList();
    }

    private static void EmitProgress(int percent, string message)
    {
        var payload = JsonSerializer.Serialize(new CreateSubtitlesWorkerProgressEvent
        {
            Type = "progress",
            Percent = Math.Clamp(percent, 0, 100),
            Message = message
        });
        Console.WriteLine(payload);
    }

    private static bool TryParseRequestPath(string[] args, out string requestPath)
    {
        requestPath = string.Empty;
        for (var index = 0; index < args.Length; index++)
        {
            if (!string.Equals(args[index], "--request", StringComparison.OrdinalIgnoreCase) || index + 1 >= args.Length)
            {
                continue;
            }

            requestPath = args[index + 1];
            return !string.IsNullOrWhiteSpace(requestPath);
        }

        return false;
    }

    private static async Task<CreateSubtitlesWorkerRequest> LoadRequestAsync(string requestPath)
    {
        await using var stream = File.OpenRead(requestPath);
        var request = await JsonSerializer.DeserializeAsync<CreateSubtitlesWorkerRequest>(stream).ConfigureAwait(false);
        if (request is null)
        {
            throw new InvalidOperationException("Subtitle worker request is empty.");
        }

        return request;
    }

    private static async Task SaveResponseAsync(string responsePath, CreateSubtitlesWorkerResponse response)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(responsePath)!);
        await File.WriteAllTextAsync(responsePath, JsonSerializer.Serialize(response, s_jsonOptions), Encoding.UTF8).ConfigureAwait(false);
    }

    private static bool IsCancellationRequested(string cancelSignalPath) =>
        !string.IsNullOrWhiteSpace(cancelSignalPath) && File.Exists(cancelSignalPath);

    private sealed class CreateSubtitlesWorkerRequest
    {
        public string EncoderPath { get; set; } = string.Empty;
        public string DecoderPath { get; set; } = string.Empty;
        public string TokensPath { get; set; } = string.Empty;
        public string WavPath { get; set; } = string.Empty;
        public string ResponsePath { get; set; } = string.Empty;
        public string CancelSignalPath { get; set; } = string.Empty;
        public int NumThreads { get; set; } = 1;
        public int WindowMilliseconds { get; set; } = 29_000;
        public int OverlapMilliseconds { get; set; } = 1_500;
        public int FeatureDim { get; set; } = 80;
    }

    private sealed class CreateSubtitlesWorkerResponse
    {
        public bool Success { get; set; }
        public bool Canceled { get; set; }
        public string? ErrorMessage { get; set; }
        public string? DetectedLanguage { get; set; }
        public double AudioDurationSeconds { get; set; }
        public List<CreateSubtitlesWorkerWord> Words { get; set; } = [];
        public string? ProviderUsed { get; set; }
    }

    private sealed class CreateSubtitlesWorkerWord
    {
        public string Text { get; set; } = string.Empty;
        public double StartSeconds { get; set; }
    }

    private sealed class CreateSubtitlesWorkerProgressEvent
    {
        public string Type { get; set; } = "progress";
        public int Percent { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    private sealed record LoadedWave(int SampleRate, int Channels, float[] Samples)
    {
        public double DurationSeconds => Samples.Length / (double)SampleRate;
    }

    private sealed record WaveWindow(int StartSample, int LengthSamples, double StartSeconds);

    private sealed record WordTiming(string Text, string NormalizedText, double StartSeconds);

    private static class WaveLoader
    {
        public static LoadedWave LoadMono16Khz(string path)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"Wave file not found: {path}", path);
            }

            using var reader = new WaveFileReader(path);
            if (reader.WaveFormat.Channels != 1)
            {
                throw new InvalidOperationException($"Expected mono WAV, got {reader.WaveFormat.Channels} channels.");
            }

            if (reader.WaveFormat.SampleRate != 16000)
            {
                throw new InvalidOperationException($"Expected 16 kHz WAV, got {reader.WaveFormat.SampleRate} Hz.");
            }

            var provider = reader.ToSampleProvider();
            var samples = new List<float>(checked((int)(reader.SampleCount / Math.Max(1, reader.WaveFormat.Channels))));
            var buffer = new float[4096];
            while (true)
            {
                var read = provider.Read(buffer, 0, buffer.Length);
                if (read == 0)
                {
                    break;
                }

                for (var index = 0; index < read; index++)
                {
                    samples.Add(buffer[index]);
                }
            }

            return new LoadedWave(reader.WaveFormat.SampleRate, reader.WaveFormat.Channels, samples.ToArray());
        }
    }

    private static class WaveWindowPlanner
    {
        public static IReadOnlyList<WaveWindow> Plan(int totalSamples, int sampleRate, int windowMilliseconds, int overlapMilliseconds)
        {
            var windows = new List<WaveWindow>();
            var cappedWindowMilliseconds = Math.Clamp(windowMilliseconds, 10_000, 29_000);
            var cappedOverlapMilliseconds = Math.Clamp(overlapMilliseconds, 0, cappedWindowMilliseconds / 3);
            var windowSamples = Math.Max(sampleRate, sampleRate * cappedWindowMilliseconds / 1000);
            var overlapSamples = sampleRate * cappedOverlapMilliseconds / 1000;
            var stepSamples = Math.Max(sampleRate / 2, windowSamples - overlapSamples);

            if (totalSamples <= windowSamples)
            {
                windows.Add(new WaveWindow(0, totalSamples, 0d));
                return windows;
            }

            var startSample = 0;
            while (startSample < totalSamples)
            {
                var remaining = totalSamples - startSample;
                var length = Math.Min(windowSamples, remaining);
                windows.Add(new WaveWindow(startSample, length, startSample / (double)sampleRate));
                if (startSample + length >= totalSamples)
                {
                    break;
                }

                startSample += stepSamples;
            }

            return windows;
        }

        public static float[] TakeLanguageWindow(float[] samples, int sampleRate)
        {
            var length = Math.Min(samples.Length, sampleRate * 20);
            var buffer = new float[length];
            Array.Copy(samples, buffer, length);
            return buffer;
        }
    }

    private static class WhisperWordBuilder
    {
        public static List<WordTiming> BuildWords(IReadOnlyList<string> tokens, IReadOnlyList<float> timestamps, double globalOffsetSeconds)
        {
            var words = new List<WordTiming>();
            WordAccumulator? current = null;
            var comparableCount = Math.Min(tokens.Count, timestamps.Count);

            for (var index = 0; index < comparableCount; index++)
            {
                var rawToken = tokens[index];
                if (string.IsNullOrWhiteSpace(rawToken))
                {
                    continue;
                }

                var trimmed = rawToken.Trim();
                if (string.IsNullOrWhiteSpace(trimmed))
                {
                    continue;
                }

                var tokenStart = globalOffsetSeconds + timestamps[index];
                var startsNewWord = rawToken[0] == ' ' || rawToken[0] == '\t' || rawToken[0] == '\n';
                var punctuationOnly = IsPunctuationOnly(trimmed);

                if (punctuationOnly)
                {
                    if (current is not null)
                    {
                        current.Text.Append(trimmed);
                    }
                    else
                    {
                        words.Add(new WordTiming(trimmed, NormalizeForComparison(trimmed), tokenStart));
                    }

                    continue;
                }

                if (current is not null && startsNewWord)
                {
                    words.Add(current.Build());
                    current = null;
                }

                current ??= new WordAccumulator(tokenStart);
                current.Text.Append(trimmed);
            }

            if (current is not null)
            {
                words.Add(current.Build());
            }

            return words;
        }

        private static bool IsPunctuationOnly(string token) =>
            token.All(static character => char.IsPunctuation(character) || char.IsSymbol(character));

        private static string NormalizeForComparison(string text)
        {
            var builder = new StringBuilder(text.Length);
            foreach (var character in text)
            {
                if (char.IsLetterOrDigit(character))
                {
                    builder.Append(char.ToLowerInvariant(character));
                    continue;
                }

                if (character is '\'' or '’' or '-' or '.' or ',' or '!' or '?' or ':' or ';')
                {
                    builder.Append(character);
                }
            }

            return builder.ToString();
        }

        private sealed class WordAccumulator
        {
            public WordAccumulator(double startSeconds)
            {
                StartSeconds = startSeconds;
            }

            public double StartSeconds { get; }

            public StringBuilder Text { get; } = new();

            public WordTiming Build()
            {
                var text = Text.ToString();
                return new WordTiming(text, NormalizeForComparison(text), StartSeconds);
            }
        }
    }

    private static class WordMerger
    {
        public static void Merge(List<WordTiming> mergedWords, IReadOnlyList<WordTiming> incomingWords)
        {
            if (incomingWords.Count == 0)
            {
                return;
            }

            if (mergedWords.Count == 0)
            {
                mergedWords.AddRange(incomingWords);
                return;
            }

            var skipCount = FindOverlapWordCount(mergedWords, incomingWords);
            for (var index = skipCount; index < incomingWords.Count; index++)
            {
                var word = incomingWords[index];
                if (mergedWords.Count > 0 && IsNearDuplicate(mergedWords[^1], word))
                {
                    continue;
                }

                mergedWords.Add(word);
            }
        }

        private static int FindOverlapWordCount(IReadOnlyList<WordTiming> mergedWords, IReadOnlyList<WordTiming> incomingWords)
        {
            var maxComparable = Math.Min(12, Math.Min(mergedWords.Count, incomingWords.Count));
            for (var candidate = maxComparable; candidate >= 1; candidate--)
            {
                var mergedStart = mergedWords.Count - candidate;
                var matches = true;
                for (var index = 0; index < candidate; index++)
                {
                    var left = mergedWords[mergedStart + index];
                    var right = incomingWords[index];
                    if (!string.Equals(left.NormalizedText, right.NormalizedText, StringComparison.Ordinal) ||
                        Math.Abs(left.StartSeconds - right.StartSeconds) > 0.80d)
                    {
                        matches = false;
                        break;
                    }
                }

                if (matches)
                {
                    return candidate;
                }
            }

            return 0;
        }

        private static bool IsNearDuplicate(WordTiming previous, WordTiming current)
        {
            return string.Equals(previous.NormalizedText, current.NormalizedText, StringComparison.Ordinal) &&
                   Math.Abs(previous.StartSeconds - current.StartSeconds) <= 0.35d;
        }
    }
}
