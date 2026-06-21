using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace FrameShift.Core.AI.CreateSubtitles;

internal sealed record CreateSubtitlesWordTiming(string Text, string NormalizedText, double StartSeconds);

internal sealed record CreateSubtitlesCue(int Index, string Text, TimeSpan Start, TimeSpan End);

internal static class CreateSubtitlesWordNormalizer
{
    public static IReadOnlyList<CreateSubtitlesWordTiming> Normalize(IReadOnlyList<CreateSubtitlesWorkerWord> words)
    {
        var normalized = new List<CreateSubtitlesWordTiming>(words.Count);
        foreach (var word in words)
        {
            var text = NormalizeText(word.Text);
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            normalized.Add(new CreateSubtitlesWordTiming(
                text,
                NormalizeForComparison(text),
                Math.Max(0d, word.StartSeconds)));
        }

        return normalized;
    }

    public static string NormalizeForComparison(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        foreach (var character in text.Trim())
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

    private static string NormalizeText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}

internal static class CreateSubtitlesSegmenter
{
    private const int MaxCueCharacters = 84;
    private const int MaxCueWords = 14;
    private const double MaxCueSeconds = 5.8d;
    private const double SilenceBreakSeconds = 0.85d;

    public static IReadOnlyList<CreateSubtitlesCue> BuildCues(
        IReadOnlyList<CreateSubtitlesWordTiming> words,
        TimeSpan totalDuration)
    {
        if (words.Count == 0)
        {
            return [];
        }

        var spans = new List<(int StartIndex, int EndIndex)>();
        var index = 0;
        while (index < words.Count)
        {
            var cueStartIndex = index;
            var cueEndIndex = index;

            while (cueEndIndex < words.Count - 1)
            {
                var current = words[cueEndIndex];
                var next = words[cueEndIndex + 1];
                var proposedText = JoinWords(words, cueStartIndex, cueEndIndex + 1);
                var cueDuration = next.StartSeconds - words[cueStartIndex].StartSeconds;
                var silenceGap = next.StartSeconds - current.StartSeconds;
                var currentWordCount = cueEndIndex - cueStartIndex + 1;

                var shouldBreak =
                    currentWordCount >= MaxCueWords ||
                    proposedText.Length > MaxCueCharacters ||
                    cueDuration >= MaxCueSeconds ||
                    (silenceGap >= SilenceBreakSeconds && currentWordCount >= 2) ||
                    (EndsWithStrongPunctuation(current.Text) && cueDuration >= 1.2d) ||
                    (EndsWithSoftPunctuation(current.Text) && proposedText.Length >= 38);

                if (shouldBreak)
                {
                    break;
                }

                cueEndIndex++;
            }

            spans.Add((cueStartIndex, cueEndIndex));
            index = cueEndIndex + 1;
        }

        var cues = new List<CreateSubtitlesCue>(spans.Count);
        for (var cueIndex = 0; cueIndex < spans.Count; cueIndex++)
        {
            var span = spans[cueIndex];
            var cueStart = words[span.StartIndex].StartSeconds;
            var lastWordStart = words[span.EndIndex].StartSeconds;
            var nextCueStart = cueIndex < spans.Count - 1
                ? words[spans[cueIndex + 1].StartIndex].StartSeconds
                : totalDuration.TotalSeconds;
            var cueEnd = ComputeCueEnd(cueStart, lastWordStart, nextCueStart, totalDuration.TotalSeconds, words[span.EndIndex].Text);

            cues.Add(new CreateSubtitlesCue(
                cueIndex + 1,
                BreakIntoLines(JoinWords(words, span.StartIndex, span.EndIndex)),
                TimeSpan.FromSeconds(cueStart),
                TimeSpan.FromSeconds(cueEnd)));
        }

        return cues;
    }

    private static double ComputeCueEnd(
        double cueStart,
        double lastWordStart,
        double nextCueStart,
        double totalDurationSeconds,
        string lastWordText)
    {
        var tail = EstimateTailSeconds(lastWordText);
        var desiredEnd = lastWordStart + tail;
        var maxAllowedEnd = nextCueStart > cueStart
            ? nextCueStart - 0.04d
            : cueStart + 0.60d;
        var resolvedEnd = Math.Min(maxAllowedEnd, desiredEnd);

        if (double.IsNaN(resolvedEnd) || double.IsInfinity(resolvedEnd))
        {
            resolvedEnd = cueStart + 0.80d;
        }

        resolvedEnd = Math.Max(resolvedEnd, cueStart + 0.35d);
        resolvedEnd = Math.Min(resolvedEnd, totalDurationSeconds);

        if (resolvedEnd <= cueStart)
        {
            resolvedEnd = Math.Min(totalDurationSeconds, cueStart + 0.60d);
        }

        return resolvedEnd;
    }

    private static double EstimateTailSeconds(string lastWordText)
    {
        var bare = lastWordText.Trim();
        var letterCount = bare.Count(static character => char.IsLetterOrDigit(character));
        var estimate = 0.32d + (letterCount * 0.035d);
        if (EndsWithStrongPunctuation(lastWordText))
        {
            estimate += 0.12d;
        }

        return Math.Clamp(estimate, 0.38d, 1.10d);
    }

    private static bool EndsWithStrongPunctuation(string text) =>
        text.EndsWith(".", StringComparison.Ordinal) ||
        text.EndsWith("!", StringComparison.Ordinal) ||
        text.EndsWith("?", StringComparison.Ordinal);

    private static bool EndsWithSoftPunctuation(string text) =>
        text.EndsWith(",", StringComparison.Ordinal) ||
        text.EndsWith(";", StringComparison.Ordinal) ||
        text.EndsWith(":", StringComparison.Ordinal);

    private static string JoinWords(IReadOnlyList<CreateSubtitlesWordTiming> words, int startIndex, int endIndex)
    {
        var builder = new StringBuilder();
        for (var index = startIndex; index <= endIndex; index++)
        {
            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append(words[index].Text);
        }

        return builder.ToString().Trim();
    }

    private static string BreakIntoLines(string text)
    {
        const int singleLineTarget = 42;
        if (text.Length <= singleLineTarget)
        {
            return text;
        }

        var midpoint = text.Length / 2;
        var bestBreak = -1;
        var bestDistance = int.MaxValue;

        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] != ' ')
            {
                continue;
            }

            var distance = Math.Abs(index - midpoint);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestBreak = index;
            }
        }

        if (bestBreak <= 0 || bestBreak >= text.Length - 1)
        {
            return text;
        }

        return $"{text[..bestBreak].Trim()}{Environment.NewLine}{text[(bestBreak + 1)..].Trim()}";
    }
}

internal static class CreateSubtitlesSrtFormatter
{
    public static string Format(IReadOnlyList<CreateSubtitlesCue> cues)
    {
        var builder = new StringBuilder(cues.Count * 48);
        foreach (var cue in cues)
        {
            builder
                .Append(cue.Index.ToString(CultureInfo.InvariantCulture))
                .AppendLine()
                .Append(FormatTimestamp(cue.Start))
                .Append(" --> ")
                .Append(FormatTimestamp(cue.End))
                .AppendLine()
                .Append(cue.Text)
                .AppendLine()
                .AppendLine();
        }

        return builder.ToString();
    }

    private static string FormatTimestamp(TimeSpan timeSpan)
    {
        if (timeSpan < TimeSpan.Zero)
        {
            timeSpan = TimeSpan.Zero;
        }

        return string.Create(CultureInfo.InvariantCulture, $"{(int)timeSpan.TotalHours:00}:{timeSpan.Minutes:00}:{timeSpan.Seconds:00},{timeSpan.Milliseconds:000}");
    }
}
