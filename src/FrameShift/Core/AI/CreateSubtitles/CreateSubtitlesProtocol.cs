using System.Collections.Generic;

namespace FrameShift.Core.AI.CreateSubtitles;

internal sealed class CreateSubtitlesWorkerRequest
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

internal sealed class CreateSubtitlesWorkerProgressEvent
{
    public string Type { get; set; } = "progress";
    public int Percent { get; set; }
    public string Message { get; set; } = string.Empty;
}

internal sealed class CreateSubtitlesWorkerWord
{
    public string Text { get; set; } = string.Empty;
    public double StartSeconds { get; set; }
}

internal sealed class CreateSubtitlesWorkerToken
{
    public string RawText { get; set; } = string.Empty;
    public string TrimmedText { get; set; } = string.Empty;
    public double StartSeconds { get; set; }
    public bool StartsNewWord { get; set; }
    public bool IsWhitespaceOnly { get; set; }
    public bool IsPunctuationOnly { get; set; }
}

internal sealed class CreateSubtitlesWorkerResponse
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
