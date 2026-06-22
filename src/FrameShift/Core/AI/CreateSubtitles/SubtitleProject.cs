using System;
using System.Collections.Generic;

namespace FrameShift.Core.AI.CreateSubtitles;

internal sealed record SubtitleWord(
    string Text,
    string NormalizedText,
    double StartSeconds,
    double EndSeconds,
    bool IsTimingReliable);

internal sealed record SubtitleSegment(
    int Index,
    string Text,
    TimeSpan Start,
    TimeSpan End,
    bool HasReliableWordAlignment,
    IReadOnlyList<SubtitleWord> Words,
    TimeSpan? RefinedDisplayStart = null)
{
    public TimeSpan DisplayStart =>
        RefinedDisplayStart.HasValue && RefinedDisplayStart.Value > Start
            ? RefinedDisplayStart.Value
            : Start;

    public bool HasRefinedDisplayStart =>
        RefinedDisplayStart.HasValue && RefinedDisplayStart.Value > Start;
}

internal sealed record SubtitleProject(
    TimeSpan TotalDuration,
    IReadOnlyList<SubtitleSegment> Segments);
