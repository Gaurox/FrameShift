namespace FrameShift.Core.Actions;

public sealed record BalanceVolumeSettings(string Intensity = "moderate")
{
    public const string OptionKey = "volume-intensity";

    public static bool TryParse(string? value, out BalanceVolumeSettings settings)
    {
        var intensity = value is null ? "moderate" : value.Trim().ToLowerInvariant();
        settings = new BalanceVolumeSettings(intensity);
        return intensity is "light" or "moderate" or "strong";
    }

    // Linked channels preserve the stereo/surround image. The gain cap and silence
    // threshold avoid turning quiet ambience into excessive background noise.
    public string AudioFilter => Intensity switch
    {
        "light" => BuildFilter("-18dB", "2", "2"),
        "moderate" => BuildFilter("-22dB", "4", "4"),
        "strong" => BuildFilter("-26dB", "8", "8"),
        _ => throw new ArgumentOutOfRangeException(nameof(Intensity))
    };

    private static string BuildFilter(string threshold, string ratio, string maxGain) =>
        $"acompressor=threshold={threshold}:ratio={ratio}:attack=20:release=250:link=maximum," +
        $"dynaudnorm=f=250:g=31:p=0.9:m={maxGain}:r=0.15:n=1:c=1:t=0.003," +
        "alimiter=limit=0.95:level=false:latency=true";
}
