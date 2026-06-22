using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace FrameShift.Core.AI.CreateSubtitles;

internal static class CreateSubtitlesWordNormalizer
{
    public static IReadOnlyList<SubtitleWord> Normalize(IReadOnlyList<CreateSubtitlesWorkerWord> words)
    {
        var normalized = new List<SubtitleWord>(words.Count);
        foreach (var word in words)
        {
            var text = NormalizeText(word.Text);
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            var startSeconds = Math.Max(0d, word.StartSeconds);
            normalized.Add(new SubtitleWord(
                text,
                NormalizeForComparison(text),
                startSeconds,
                startSeconds,
                false));
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

internal static class CreateSubtitlesProjectBuilder
{
    public static SubtitleProject Build(IReadOnlyList<CreateSubtitlesWorkerWord> words, TimeSpan totalDuration)
    {
        var normalizedWords = CreateSubtitlesWordNormalizer.Normalize(words);
        var segments = CreateSubtitlesSegmenter.BuildSegments(normalizedWords, totalDuration);
        return new SubtitleProject(totalDuration, segments);
    }
}

internal static class CreateSubtitlesSegmenter
{
    private const int MaxCueCharacters = 84;
    private const int MaxCueWords = 14;
    private const double MaxCueSeconds = 5.8d;
    private const double SilenceBreakSeconds = 0.85d;

    public static IReadOnlyList<SubtitleSegment> BuildSegments(
        IReadOnlyList<SubtitleWord> words,
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

        var segments = new List<SubtitleSegment>(spans.Count);
        for (var cueIndex = 0; cueIndex < spans.Count; cueIndex++)
        {
            var span = spans[cueIndex];
            var cueStart = words[span.StartIndex].StartSeconds;
            var lastWordStart = words[span.EndIndex].StartSeconds;
            var nextCueStart = cueIndex < spans.Count - 1
                ? words[spans[cueIndex + 1].StartIndex].StartSeconds
                : totalDuration.TotalSeconds;
            var cueEnd = ComputeCueEnd(cueStart, lastWordStart, nextCueStart, totalDuration.TotalSeconds, words[span.EndIndex].Text);
            var rawSegmentWords = words.Skip(span.StartIndex).Take(span.EndIndex - span.StartIndex + 1).ToArray();
            var resolvedSegmentWords = SubtitleWordTimingResolver.Resolve(rawSegmentWords, cueStart, cueEnd, out var hasReliableWordAlignment);

            segments.Add(new SubtitleSegment(
                cueIndex + 1,
                BreakIntoLines(JoinWords(words, span.StartIndex, span.EndIndex)),
                TimeSpan.FromSeconds(cueStart),
                TimeSpan.FromSeconds(cueEnd),
                hasReliableWordAlignment,
                resolvedSegmentWords));
        }

        return segments;
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

    private static string JoinWords(IReadOnlyList<SubtitleWord> words, int startIndex, int endIndex)
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

    private static class SubtitleWordTimingResolver
    {
        private const double MinimumValidGapSeconds = 0.001d;
        private const double SegmentToleranceSeconds = 0.050d;

        public static IReadOnlyList<SubtitleWord> Resolve(
            IReadOnlyList<SubtitleWord> words,
            double segmentStartSeconds,
            double segmentEndSeconds,
            out bool hasReliableWordAlignment)
        {
            if (words.Count == 0)
            {
                hasReliableWordAlignment = false;
                return [];
            }

            if (TryResolveReliable(words, segmentStartSeconds, segmentEndSeconds, out var resolvedWords))
            {
                hasReliableWordAlignment = true;
                return resolvedWords;
            }

            hasReliableWordAlignment = false;
            return BuildFallback(words, segmentStartSeconds, segmentEndSeconds);
        }

        private static bool TryResolveReliable(
            IReadOnlyList<SubtitleWord> words,
            double segmentStartSeconds,
            double segmentEndSeconds,
            out SubtitleWord[] resolvedWords)
        {
            resolvedWords = new SubtitleWord[words.Count];

            if (!IsFinite(segmentStartSeconds) ||
                !IsFinite(segmentEndSeconds) ||
                segmentEndSeconds <= segmentStartSeconds + MinimumValidGapSeconds)
            {
                return false;
            }

            for (var index = 0; index < words.Count; index++)
            {
                var word = words[index];
                var startSeconds = word.StartSeconds;
                if (!IsFinite(startSeconds))
                {
                    return false;
                }

                if (startSeconds < segmentStartSeconds - SegmentToleranceSeconds ||
                    startSeconds >= segmentEndSeconds - MinimumValidGapSeconds)
                {
                    return false;
                }

                if (index > 0 && startSeconds <= resolvedWords[index - 1].StartSeconds + MinimumValidGapSeconds)
                {
                    return false;
                }

                var endSeconds = index < words.Count - 1
                    ? words[index + 1].StartSeconds
                    : segmentEndSeconds;

                if (!IsFinite(endSeconds) ||
                    endSeconds > segmentEndSeconds + SegmentToleranceSeconds ||
                    endSeconds <= startSeconds + MinimumValidGapSeconds)
                {
                    return false;
                }

                resolvedWords[index] = word with
                {
                    EndSeconds = Math.Min(endSeconds, segmentEndSeconds),
                    IsTimingReliable = true
                };
            }

            return true;
        }

        private static IReadOnlyList<SubtitleWord> BuildFallback(
            IReadOnlyList<SubtitleWord> words,
            double segmentStartSeconds,
            double segmentEndSeconds)
        {
            var safeStartSeconds = IsFinite(segmentStartSeconds) ? Math.Max(0d, segmentStartSeconds) : 0d;
            var safeEndSeconds = IsFinite(segmentEndSeconds)
                ? Math.Max(safeStartSeconds + 0.01d, segmentEndSeconds)
                : safeStartSeconds + Math.Max(0.35d, words.Count * 0.20d);
            var segmentDurationSeconds = safeEndSeconds - safeStartSeconds;
            var stepSeconds = segmentDurationSeconds / words.Count;

            var resolvedWords = new SubtitleWord[words.Count];
            for (var index = 0; index < words.Count; index++)
            {
                var startSeconds = safeStartSeconds + (stepSeconds * index);
                var endSeconds = index == words.Count - 1
                    ? safeEndSeconds
                    : safeStartSeconds + (stepSeconds * (index + 1));

                resolvedWords[index] = words[index] with
                {
                    StartSeconds = startSeconds,
                    EndSeconds = endSeconds,
                    IsTimingReliable = false
                };
            }

            return resolvedWords;
        }

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}

internal static class CreateSubtitlesSrtFormatter
{
    public static string Format(SubtitleProject project)
    {
        var builder = new StringBuilder(project.Segments.Count * 48);
        foreach (var segment in project.Segments)
        {
            builder
                .Append(segment.Index.ToString(CultureInfo.InvariantCulture))
                .AppendLine()
                .Append(FormatTimestamp(segment.DisplayStart))
                .Append(" --> ")
                .Append(FormatTimestamp(segment.End))
                .AppendLine()
                .Append(segment.Text)
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
