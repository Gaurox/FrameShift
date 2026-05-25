namespace FrameShift.Core.AI.RemoveNoise;

internal static class RemoveNoiseVideoSettings
{
    public const string StrengthLight   = "light";
    public const string StrengthNormal  = "normal";
    public const string StrengthStrong  = "strong";
    public const string StrengthMaximum = "maximum";

    public static float ToMinGain(string? strength) => strength?.ToLowerInvariant() switch
    {
        StrengthLight   => 0.5f,
        StrengthNormal  => 0.2f,
        StrengthStrong  => 0.08f,
        _               => 0.0f
    };

    public static bool IsSupportedVideoExtension(string ext) =>
        ext is ".mp4" or ".mkv" or ".avi" or ".mov" or ".webm" or ".m4v";

    public static string GetSupportedVideoExtensionsText() =>
        ".mp4, .mkv, .avi, .mov, .webm, .m4v";

    // Option B: choose remux audio codec based on container
    public static string GetRemuxAudioCodec(string containerExt) => containerExt.ToLowerInvariant() switch
    {
        ".webm" => "libopus",
        ".avi"  => "pcm_s16le",
        _       => "aac"
    };

    public static bool RemuxAudioNeedsBitrateArg(string codec) =>
        codec is not "pcm_s16le";
}
