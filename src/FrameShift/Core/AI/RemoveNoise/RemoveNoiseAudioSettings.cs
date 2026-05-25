namespace FrameShift.Core.AI.RemoveNoise;

internal static class RemoveNoiseAudioSettings
{
    public const string StrengthLight   = RemoveNoiseVideoSettings.StrengthLight;
    public const string StrengthNormal  = RemoveNoiseVideoSettings.StrengthNormal;
    public const string StrengthStrong  = RemoveNoiseVideoSettings.StrengthStrong;
    public const string StrengthMaximum = RemoveNoiseVideoSettings.StrengthMaximum;

    public static float ToMinGain(string? strength) => RemoveNoiseVideoSettings.ToMinGain(strength);

    public static string GetOutputAudioCodec(string containerExt) => containerExt.ToLowerInvariant() switch
    {
        ".wav"  => "pcm_s16le",
        ".flac" => "flac",
        ".ogg"  => "libvorbis",
        ".m4a"  => "aac",
        _       => "libmp3lame"
    };

    public static bool OutputAudioNeedsBitrateArg(string codec) =>
        codec is not ("pcm_s16le" or "flac");
}
