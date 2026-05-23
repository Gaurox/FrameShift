using System;
using System.Collections.Generic;
using FrameShift.Core.FFprobe;

namespace FrameShift.Core.Actions;

public sealed record ExtractAudioTarget(string Id, string DisplayName, string Description) : IConversionChoice;

public sealed record ExtractAudioPlan(
    string TargetId,
    string OutputExtension,
    string Codec,
    IReadOnlyList<string> Args,
    bool CopyAudio,
    string StrategyDescription);

public static class ExtractAudioCatalog
{
    private static readonly ExtractAudioTarget[] Targets =
    [
        new("mp3", "MP3", "Extract the first audio track to MP3."),
        new("wav", "WAV", "Extract the first audio track to WAV."),
        new("flac", "FLAC", "Extract the first audio track to FLAC."),
        new("m4a", "M4A", "Extract the first audio track to M4A."),
        new("ogg", "OGG", "Extract the first audio track to OGG.")
    ];

    public static IReadOnlyList<IConversionChoice> GetTargets() => Targets;

    public static bool IsSupportedTarget(string id)
    {
        foreach (var target in Targets)
        {
            if (string.Equals(target.Id, id, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static ExtractAudioTarget GetTargetById(string id)
    {
        foreach (var target in Targets)
        {
            if (string.Equals(target.Id, id, StringComparison.OrdinalIgnoreCase))
            {
                return target;
            }
        }

        return Targets[0];
    }

    public static ExtractAudioPlan CreatePlan(string targetId, MediaProbeResult probe)
    {
        var normalizedTarget = targetId.Trim().ToLowerInvariant();
        var sourceCodec = probe.AudioCodecs.Count > 0
            ? NormalizeCodec(probe.AudioCodecs[0])
            : string.Empty;

        return normalizedTarget switch
        {
            "mp3" => sourceCodec == "mp3"
                ? new ExtractAudioPlan("mp3", ".mp3", "copy", [], true, "copy mp3 stream")
                : new ExtractAudioPlan("mp3", ".mp3", "libmp3lame", ["-b:a", "320k"], false, "encode mp3 320k"),
            "wav" => IsWaveCopyCodec(sourceCodec)
                ? new ExtractAudioPlan("wav", ".wav", "copy", [], true, $"copy {sourceCodec} stream")
                : new ExtractAudioPlan("wav", ".wav", "pcm_s16le", [], false, "encode pcm_s16le"),
            "flac" => sourceCodec == "flac"
                ? new ExtractAudioPlan("flac", ".flac", "copy", [], true, "copy flac stream")
                : new ExtractAudioPlan("flac", ".flac", "flac", ["-compression_level", "5"], false, "encode flac level 5"),
            "m4a" => sourceCodec is "aac" or "alac"
                ? new ExtractAudioPlan("m4a", ".m4a", "copy", [], true, $"copy {sourceCodec} stream")
                : new ExtractAudioPlan("m4a", ".m4a", "aac", ["-b:a", "256k"], false, "encode aac 256k"),
            "ogg" => sourceCodec is "vorbis" or "opus"
                ? new ExtractAudioPlan("ogg", ".ogg", "copy", [], true, $"copy {sourceCodec} stream")
                : new ExtractAudioPlan("ogg", ".ogg", "libvorbis", ["-q:a", "6"], false, "encode vorbis q6"),
            _ => throw new NotSupportedException($"Unsupported extraction target '{targetId}'.")
        };
    }

    private static bool IsWaveCopyCodec(string codec)
    {
        return codec.StartsWith("pcm_", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeCodec(string codec)
    {
        return string.IsNullOrWhiteSpace(codec)
            ? string.Empty
            : codec.Trim().ToLowerInvariant();
    }
}
