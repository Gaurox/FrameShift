using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
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
        L.Initialize(Environment.GetEnvironmentVariable("FRAMESHIFT_UI_LANGUAGE"));
        if (!TryParseRequestPath(args, out var requestPath))
        {
            await Console.Error.WriteLineAsync("Missing required argument: --request <path>").ConfigureAwait(false);
            return 1;
        }

        var request = await LoadRequestAsync(requestPath).ConfigureAwait(false);
        var response = new CreateSubtitlesWorkerResponse();

        try
        {
            using var wave = new WaveWindowReader(request.WavPath);
            response.AudioDurationSeconds = wave.DurationSeconds;

            if (IsCancellationRequested(request.CancelSignalPath))
            {
                response.Canceled = true;
                response.Success = false;
                await SaveResponseAsync(request.ResponsePath, response).ConfigureAwait(false);
                return 0;
            }

            EmitProgress(2, L.Get("Program_Loading_Whisper_language_detector_07a4bcd4"));
            response.DetectedLanguage = DetectLanguage(request, wave);

            EmitProgress(18, L.Get("Program_Loading_Whisper_recognizer_DirectML_5d01f943"));
            var (recognizer, providerUsed) = CreateRecognizer(request);
            response.ProviderUsed = providerUsed;

            List<CreateSubtitlesWorkerWord> words = [];
            List<CreateSubtitlesWorkerToken> tokens = [];
            try
            {
                using (recognizer)
                {
                    EmitProgress(20, L.Format("Program_Whisper_recognizer_ready_0_a43c8890",(providerUsed == "directml" ? "DirectML" : "CPU")));
                    (words, tokens) = TranscribeAllWindows(request, recognizer, wave);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception dmlEx) when (providerUsed == "directml")
            {
                // DirectML inference failed after successful init — restart from scratch on CPU
                Console.Error.WriteLine($"[SubtitlesWorker] DirectML inference failed, retrying with CPU: {dmlEx.Message}");
                EmitProgress(18, L.Get("Program_Retrying_with_Whisper_recognizer_CPU_668cc38b"));
                using var cpuRecognizer = BuildRecognizer(request, "cpu");
                EmitProgress(20, L.Get("Program_Whisper_recognizer_ready_CPU_a448f98e"));
                response.ProviderUsed = "cpu";
                (words, tokens) = TranscribeAllWindows(request, cpuRecognizer, wave);
            }

            response.Words = words;
            response.Tokens = tokens;

            if (IsCancellationRequested(request.CancelSignalPath))
            {
                response.Canceled = true;
                response.Success = false;
            }
            else
            {
                response.Success = true;
            }

            EmitProgress(100, L.Get("Program_Whisper_transcription_finished_5936953d"));
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

    private static string DetectLanguage(CreateSubtitlesWorkerRequest request, WaveWindowReader wave)
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
        var detectionWindow = wave.Read(0, (int)Math.Min(wave.SampleCount, WaveWindowReader.SampleRate * 20), () => IsCancellationRequested(request.CancelSignalPath));
        stream.AcceptWaveform(WaveWindowReader.SampleRate, detectionWindow);
        EmitProgress(10, L.Get("Program_Detecting_language_7dfe2221"));
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

    private static (List<CreateSubtitlesWorkerWord> Words, List<CreateSubtitlesWorkerToken> Tokens) TranscribeAllWindows(
        CreateSubtitlesWorkerRequest request,
        OfflineRecognizer recognizer,
        WaveWindowReader wave)
    {
        var windows = wave.Plan(request.WindowMilliseconds, request.OverlapMilliseconds);
        var mergedWords = new List<WordTiming>();
        var mergedTokens = new List<TokenTiming>();

        for (var index = 0; index < windows.Count; index++)
        {
            if (IsCancellationRequested(request.CancelSignalPath))
            {
                throw new OperationCanceledException("Cancellation signal detected before the next Whisper window.");
            }

            var window = windows[index];
            EmitProgress(
                20 + (int)Math.Round((index / (double)Math.Max(1, windows.Count)) * 72d, MidpointRounding.AwayFromZero),
                L.Format("Program_Transcribing_window_0_1_b5dcf260",index + 1,windows.Count));

            var buffer = wave.Read(window.StartSample, window.LengthSamples,
                () => IsCancellationRequested(request.CancelSignalPath));

            using var stream = recognizer.CreateStream();
            stream.AcceptWaveform(WaveWindowReader.SampleRate, buffer);
            recognizer.Decode(stream);

            var result = stream.Result;
            var localTokens = WhisperTokenBuilder.BuildTokens(
                result.Tokens ?? Array.Empty<string>(),
                result.Timestamps ?? Array.Empty<float>(),
                window.StartSeconds);
            var localWords = WhisperWordBuilder.BuildWords(
                result.Tokens ?? Array.Empty<string>(),
                result.Timestamps ?? Array.Empty<float>(),
                window.StartSeconds);

            TokenMerger.Merge(mergedTokens, localTokens);
            WordMerger.Merge(mergedWords, localWords);
        }

        EmitProgress(96, L.Get("Program_Finalizing_merged_timestamps_47bc304d"));
        return (
            mergedWords
                .Select(word => new CreateSubtitlesWorkerWord
                {
                    Text = word.Text,
                    StartSeconds = word.StartSeconds
                })
                .ToList(),
            mergedTokens
                .Select(token => new CreateSubtitlesWorkerToken
                {
                    RawText = token.RawText,
                    TrimmedText = token.TrimmedText,
                    StartSeconds = token.StartSeconds,
                    StartsNewWord = token.StartsNewWord,
                    IsWhitespaceOnly = token.IsWhitespaceOnly,
                    IsPunctuationOnly = token.IsPunctuationOnly
                })
                .ToList());
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
        public List<CreateSubtitlesWorkerToken> Tokens { get; set; } = [];
        public string? ProviderUsed { get; set; }
    }

    private sealed class CreateSubtitlesWorkerWord
    {
        public string Text { get; set; } = string.Empty;
        public double StartSeconds { get; set; }
    }

    private sealed class CreateSubtitlesWorkerToken
    {
        public string RawText { get; set; } = string.Empty;
        public string TrimmedText { get; set; } = string.Empty;
        public double StartSeconds { get; set; }
        public bool StartsNewWord { get; set; }
        public bool IsWhitespaceOnly { get; set; }
        public bool IsPunctuationOnly { get; set; }
    }

    private sealed class CreateSubtitlesWorkerProgressEvent
    {
        public string Type { get; set; } = "progress";
        public int Percent { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    private sealed record WordTiming(string Text, string NormalizedText, double StartSeconds);

    private sealed record TokenTiming(
        string RawText,
        string TrimmedText,
        string ComparableText,
        double StartSeconds,
        bool StartsNewWord,
        bool IsWhitespaceOnly,
        bool IsPunctuationOnly);

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

        internal static bool IsPunctuationOnly(string token) =>
            token.All(static character => char.IsPunctuation(character) || char.IsSymbol(character));

        internal static string NormalizeForComparison(string text)
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

    private static class WhisperTokenBuilder
    {
        public static List<TokenTiming> BuildTokens(IReadOnlyList<string> tokens, IReadOnlyList<float> timestamps, double globalOffsetSeconds)
        {
            var resolved = new List<TokenTiming>(Math.Min(tokens.Count, timestamps.Count));
            var comparableCount = Math.Min(tokens.Count, timestamps.Count);
            for (var index = 0; index < comparableCount; index++)
            {
                var rawToken = tokens[index] ?? string.Empty;
                var trimmedToken = rawToken.Trim();
                var isWhitespaceOnly = string.IsNullOrWhiteSpace(rawToken);
                var isPunctuationOnly = !isWhitespaceOnly && WhisperWordBuilder.IsPunctuationOnly(trimmedToken);
                var startsNewWord = rawToken.Length > 0 &&
                                    (rawToken[0] == ' ' || rawToken[0] == '\t' || rawToken[0] == '\n');
                var comparableText = BuildComparableText(trimmedToken, isWhitespaceOnly, isPunctuationOnly);

                resolved.Add(new TokenTiming(
                    rawToken,
                    trimmedToken,
                    comparableText,
                    globalOffsetSeconds + timestamps[index],
                    startsNewWord,
                    isWhitespaceOnly,
                    isPunctuationOnly));
            }

            return resolved;
        }

        private static string BuildComparableText(string trimmedToken, bool isWhitespaceOnly, bool isPunctuationOnly)
        {
            if (isWhitespaceOnly)
            {
                return "<whitespace>";
            }

            if (isPunctuationOnly)
            {
                return $"<punct:{trimmedToken}>";
            }

            var normalized = WhisperWordBuilder.NormalizeForComparison(trimmedToken);
            return string.IsNullOrWhiteSpace(normalized)
                ? trimmedToken
                : normalized;
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

    private static class TokenMerger
    {
        public static void Merge(List<TokenTiming> mergedTokens, IReadOnlyList<TokenTiming> incomingTokens)
        {
            if (incomingTokens.Count == 0)
            {
                return;
            }

            if (mergedTokens.Count == 0)
            {
                mergedTokens.AddRange(incomingTokens);
                return;
            }

            var skipCount = FindOverlapTokenCount(mergedTokens, incomingTokens);
            for (var index = skipCount; index < incomingTokens.Count; index++)
            {
                var token = incomingTokens[index];
                if (mergedTokens.Count > 0 && IsNearDuplicate(mergedTokens[^1], token))
                {
                    continue;
                }

                mergedTokens.Add(token);
            }
        }

        private static int FindOverlapTokenCount(IReadOnlyList<TokenTiming> mergedTokens, IReadOnlyList<TokenTiming> incomingTokens)
        {
            var maxComparable = Math.Min(24, Math.Min(mergedTokens.Count, incomingTokens.Count));
            for (var candidate = maxComparable; candidate >= 1; candidate--)
            {
                var mergedStart = mergedTokens.Count - candidate;
                var matches = true;
                for (var index = 0; index < candidate; index++)
                {
                    var left = mergedTokens[mergedStart + index];
                    var right = incomingTokens[index];
                    if (!string.Equals(left.ComparableText, right.ComparableText, StringComparison.Ordinal) ||
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

        private static bool IsNearDuplicate(TokenTiming previous, TokenTiming current)
        {
            return string.Equals(previous.ComparableText, current.ComparableText, StringComparison.Ordinal) &&
                   Math.Abs(previous.StartSeconds - current.StartSeconds) <= 0.30d;
        }
    }
}
