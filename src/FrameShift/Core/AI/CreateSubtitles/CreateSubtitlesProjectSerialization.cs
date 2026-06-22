using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace FrameShift.Core.AI.CreateSubtitles;

internal static class CreateSubtitlesProjectSerializer
{
    public const string FormatId = "frameshift-subtitle-project";
    public const int CurrentVersion = 1;
    public const string FileExtension = ".frameshift-subtitles.json";

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static string Serialize(SubtitleProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var payload = new SubtitleProjectFilePayload
        {
            Format = FormatId,
            Version = CurrentVersion,
            TotalDurationSeconds = project.TotalDuration.TotalSeconds,
            Segments = project.Segments.Select(segment => new SubtitleSegmentPayload
            {
                Index = segment.Index,
                Text = segment.Text,
                StartSeconds = segment.Start.TotalSeconds,
                DisplayStartSeconds = segment.RefinedDisplayStart?.TotalSeconds,
                EndSeconds = segment.End.TotalSeconds,
                HasReliableWordAlignment = segment.HasReliableWordAlignment,
                Words = segment.Words.Select(word => new SubtitleWordPayload
                {
                    Text = word.Text,
                    NormalizedText = word.NormalizedText,
                    StartSeconds = word.StartSeconds,
                    EndSeconds = word.EndSeconds,
                    IsTimingReliable = word.IsTimingReliable
                }).ToList()
            }).ToList()
        };

        return JsonSerializer.Serialize(payload, s_jsonOptions);
    }

    public static SubtitleProject Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidOperationException("Subtitle project JSON is empty.");
        }

        var payload = JsonSerializer.Deserialize<SubtitleProjectFilePayload>(json, s_jsonOptions);
        if (payload is null)
        {
            throw new InvalidOperationException("Subtitle project JSON is unreadable.");
        }

        if (!string.Equals(payload.Format, FormatId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Unsupported subtitle project format '{payload.Format ?? "<null>"}'.");
        }

        if (payload.Version != CurrentVersion)
        {
            throw new InvalidOperationException($"Unsupported subtitle project version '{payload.Version}'.");
        }

        var segments = payload.Segments.Select(segment => new SubtitleSegment(
            segment.Index,
            segment.Text ?? string.Empty,
            TimeSpan.FromSeconds(Math.Max(0d, segment.StartSeconds)),
            TimeSpan.FromSeconds(Math.Max(0d, segment.EndSeconds)),
            segment.HasReliableWordAlignment,
            segment.Words.Select(word => new SubtitleWord(
                word.Text ?? string.Empty,
                word.NormalizedText ?? string.Empty,
                Math.Max(0d, word.StartSeconds),
                Math.Max(0d, word.EndSeconds),
                word.IsTimingReliable)).ToArray(),
            segment.DisplayStartSeconds.HasValue
                ? TimeSpan.FromSeconds(Math.Max(0d, segment.DisplayStartSeconds.Value))
                : null)).ToArray();

        return new SubtitleProject(
            TimeSpan.FromSeconds(Math.Max(0d, payload.TotalDurationSeconds)),
            segments);
    }

    private sealed class SubtitleProjectFilePayload
    {
        public string Format { get; set; } = string.Empty;
        public int Version { get; set; }
        public double TotalDurationSeconds { get; set; }
        public List<SubtitleSegmentPayload> Segments { get; set; } = [];
    }

    private sealed class SubtitleSegmentPayload
    {
        public int Index { get; set; }
        public string? Text { get; set; }
        public double StartSeconds { get; set; }
        public double? DisplayStartSeconds { get; set; }
        public double EndSeconds { get; set; }
        public bool HasReliableWordAlignment { get; set; }
        public List<SubtitleWordPayload> Words { get; set; } = [];
    }

    private sealed class SubtitleWordPayload
    {
        public string? Text { get; set; }
        public string? NormalizedText { get; set; }
        public double StartSeconds { get; set; }
        public double EndSeconds { get; set; }
        public bool IsTimingReliable { get; set; }
    }
}
