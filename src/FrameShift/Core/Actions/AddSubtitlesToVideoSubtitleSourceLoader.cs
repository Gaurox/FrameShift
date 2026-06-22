using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FrameShift.Core.AI.CreateSubtitles;
using FrameShift.Core.FFprobe;

namespace FrameShift.Core.Actions;

internal enum AddSubtitlesToVideoSubtitleSourceKind
{
    Srt,
    Ass,
    FrameShiftProject
}

internal sealed record AddSubtitlesToVideoPreparedSubtitleInput(
    string AssFilePath,
    bool DeleteAfterUse,
    string PreparationSummary,
    AddSubtitlesToVideoSubtitleSourceKind SourceKind);

internal static class AddSubtitlesToVideoSubtitleSourceLoader
{
    public static async Task<AddSubtitlesToVideoPreparedSubtitleInput> PrepareAssInputAsync(
        string subtitleFilePath,
        MediaProbeResult probe,
        AddSubtitlesToVideoBurnSettings burnSettings,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subtitleFilePath);

        var sourceKind = DetectSourceKind(subtitleFilePath);
        if (sourceKind == AddSubtitlesToVideoSubtitleSourceKind.Ass)
        {
            var copiedAssPath = CreateTemporaryAssPath();
            File.Copy(subtitleFilePath, copiedAssPath, overwrite: false);
            return new AddSubtitlesToVideoPreparedSubtitleInput(
                copiedAssPath,
                DeleteAfterUse: true,
                "Copied ASS subtitle file to a temporary working path while preserving its style.",
                sourceKind);
        }

        var project = await LoadProjectAsync(subtitleFilePath, sourceKind, cancellationToken).ConfigureAwait(false);
        var appearance = (burnSettings ?? AddSubtitlesToVideoBurnSettings.Default).ResolveAppearanceForVideo(probe);
        var layout = appearance.ToAssLayout(probe.DisplayVideoWidth, probe.DisplayVideoHeight);
        var assText = CreateSubtitlesAssFormatter.Format(project, appearance.AssPreset, layout);
        var tempPath = CreateTemporaryAssPath();

        await File.WriteAllTextAsync(tempPath, assText, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken).ConfigureAwait(false);

        var summary = sourceKind switch
        {
            AddSubtitlesToVideoSubtitleSourceKind.FrameShiftProject => "Generated temporary ASS from FrameShift subtitle project.",
            _ => "Generated temporary ASS from SRT subtitle file."
        };

        return new AddSubtitlesToVideoPreparedSubtitleInput(
            tempPath,
            DeleteAfterUse: true,
            summary,
            sourceKind);
    }

    public static AddSubtitlesToVideoSubtitleSourceKind DetectSourceKind(string subtitleFilePath)
    {
        if (subtitleFilePath.EndsWith(CreateSubtitlesProjectSerializer.FileExtension, StringComparison.OrdinalIgnoreCase))
        {
            return AddSubtitlesToVideoSubtitleSourceKind.FrameShiftProject;
        }

        return Path.GetExtension(subtitleFilePath).Trim().ToLowerInvariant() switch
        {
            ".ass" => AddSubtitlesToVideoSubtitleSourceKind.Ass,
            _ => AddSubtitlesToVideoSubtitleSourceKind.Srt
        };
    }

    private static async Task<SubtitleProject> LoadProjectAsync(
        string subtitleFilePath,
        AddSubtitlesToVideoSubtitleSourceKind sourceKind,
        CancellationToken cancellationToken)
    {
        var text = await ReadTextWithWindowsFallbackAsync(subtitleFilePath, cancellationToken).ConfigureAwait(false);
        return sourceKind switch
        {
            AddSubtitlesToVideoSubtitleSourceKind.FrameShiftProject => CreateSubtitlesProjectSerializer.Deserialize(text),
            _ => ParseSrt(text)
        };
    }

    private static async Task<string> ReadTextWithWindowsFallbackAsync(string path, CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        if (bytes.Length == 0)
        {
            return string.Empty;
        }

        if (HasUtf8Bom(bytes))
        {
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }

        if (HasUtf16LeBom(bytes))
        {
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        }

        if (HasUtf16BeBom(bytes))
        {
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        }

        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Default.GetString(bytes);
        }
    }

    private static string CreateTemporaryAssPath()
    {
        return Path.Combine(
            Path.GetTempPath(),
            $"frameshift_burn_subtitles_{Guid.NewGuid():N}.ass");
    }

    private static bool HasUtf8Bom(byte[] bytes)
    {
        return bytes.Length >= 3 &&
               bytes[0] == 0xEF &&
               bytes[1] == 0xBB &&
               bytes[2] == 0xBF;
    }

    private static bool HasUtf16LeBom(byte[] bytes)
    {
        return bytes.Length >= 2 &&
               bytes[0] == 0xFF &&
               bytes[1] == 0xFE;
    }

    private static bool HasUtf16BeBom(byte[] bytes)
    {
        return bytes.Length >= 2 &&
               bytes[0] == 0xFE &&
               bytes[1] == 0xFF;
    }

    private static SubtitleProject ParseSrt(string srtText)
    {
        if (string.IsNullOrWhiteSpace(srtText))
        {
            throw new InvalidOperationException("Subtitle file is empty.");
        }

        var normalized = srtText.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();
        var blocks = Regex.Split(normalized, @"\n\s*\n", RegexOptions.CultureInvariant)
            .Where(static block => !string.IsNullOrWhiteSpace(block))
            .ToArray();

        if (blocks.Length == 0)
        {
            throw new InvalidOperationException("Subtitle file does not contain any subtitle block.");
        }

        var segments = new List<SubtitleSegment>(blocks.Length);
        for (var index = 0; index < blocks.Length; index++)
        {
            var lines = blocks[index]
                .Split('\n')
                .Select(static line => line.Trim())
                .Where(static line => line.Length > 0)
                .ToArray();

            if (lines.Length == 0)
            {
                continue;
            }

            var timingLineIndex = Array.FindIndex(lines, static line => line.Contains("-->", StringComparison.Ordinal));
            if (timingLineIndex < 0 || timingLineIndex >= lines.Length)
            {
                continue;
            }

            var timingParts = lines[timingLineIndex].Split("-->", StringSplitOptions.TrimEntries);
            if (timingParts.Length != 2 ||
                !TryParseSrtTimestamp(timingParts[0], out var start) ||
                !TryParseSrtTimestamp(timingParts[1], out var end))
            {
                continue;
            }

            var textLines = lines.Skip(timingLineIndex + 1).ToArray();
            var text = string.Join(Environment.NewLine, textLines).Trim();
            if (text.Length == 0)
            {
                continue;
            }

            var words = BuildUnreliableWords(text, start, end);
            var safeEnd = end <= start ? start + TimeSpan.FromMilliseconds(10) : end;
            segments.Add(new SubtitleSegment(
                segments.Count + 1,
                text,
                start,
                safeEnd,
                HasReliableWordAlignment: false,
                words));
        }

        if (segments.Count == 0)
        {
            throw new InvalidOperationException("Subtitle file does not contain readable timings.");
        }

        var totalDuration = segments[^1].End;
        return new SubtitleProject(totalDuration, segments);
    }

    private static IReadOnlyList<SubtitleWord> BuildUnreliableWords(string text, TimeSpan start, TimeSpan end)
    {
        var tokens = Regex.Matches(text, @"\S+", RegexOptions.CultureInvariant)
            .Select(static match => match.Value)
            .ToArray();

        if (tokens.Length == 0)
        {
            return
            [
                new SubtitleWord(text, text, start.TotalSeconds, Math.Max(start.TotalSeconds + 0.01d, end.TotalSeconds), false)
            ];
        }

        var safeEnd = end <= start ? start + TimeSpan.FromMilliseconds(10) : end;
        var durationSeconds = Math.Max(0.01d, safeEnd.Subtract(start).TotalSeconds);
        var sliceDuration = durationSeconds / tokens.Length;
        var words = new SubtitleWord[tokens.Length];

        for (var index = 0; index < tokens.Length; index++)
        {
            var wordStart = start.TotalSeconds + (sliceDuration * index);
            var wordEnd = index == tokens.Length - 1
                ? safeEnd.TotalSeconds
                : start.TotalSeconds + (sliceDuration * (index + 1));
            words[index] = new SubtitleWord(tokens[index], tokens[index], wordStart, Math.Max(wordStart + 0.01d, wordEnd), false);
        }

        return words;
    }

    private static bool TryParseSrtTimestamp(string value, out TimeSpan timeSpan)
    {
        timeSpan = TimeSpan.Zero;
        var normalized = value.Trim().Replace(",", ".", StringComparison.Ordinal);
        var parts = normalized.Split(':');
        if (parts.Length != 3)
        {
            return false;
        }

        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var hours) ||
            !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes) ||
            !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
        {
            return false;
        }

        if (hours < 0 || minutes < 0 || seconds < 0)
        {
            return false;
        }

        timeSpan = TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds);
        return true;
    }
}
