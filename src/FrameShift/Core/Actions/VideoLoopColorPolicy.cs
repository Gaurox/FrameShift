using System.Text.RegularExpressions;
using FrameShift.Core.FFmpeg;
using FrameShift.Core.FFprobe;

namespace FrameShift.Core.Actions;

public enum VideoLoopColorKind { Sdr, Hdr, Undetermined }

/// <summary>Find Loop compatibility only; other actions keep their existing color policy.</summary>
public sealed record VideoLoopColorPolicy(VideoLoopColorKind Kind, bool CanRender, string Message)
{
    public string Filter { get; init; } = "";
    public bool ConvertsToSdr => Filter.Length > 0;
    public bool ToneMapsHdr => ConvertsToSdr && Kind == VideoLoopColorKind.Hdr;
    private static readonly SemaphoreSlim CapabilityGate = new(1, 1);
    private static readonly Dictionary<string, HashSet<string>> CapabilityCache = new(StringComparer.OrdinalIgnoreCase);

    // Conversion is applied once, before proxy reduction. No fps/setpts/trim here.
    private const string SdrTail = "sidedata=mode=delete,setparams=range=limited:color_primaries=bt709:color_trc=bt709:colorspace=bt709";

    public static VideoLoopColorPolicy Evaluate(MediaProbeResult probe)
    {
        var transfer = Normalize(probe.VideoColorTransfer);
        var primaries = Normalize(probe.VideoColorPrimaries);
        var space = Normalize(probe.VideoColorSpace);
        var range = Normalize(probe.VideoColorRange);
        var hdr = transfer is "smpte2084" or "arib-std-b67";
        if (range is not ("" or "tv" or "pc" or "limited" or "full"))
            return new(hdr ? VideoLoopColorKind.Hdr : VideoLoopColorKind.Undetermined, false, "Source color range is unsupported; check the source color metadata.");
        if (hdr)
        {
            if (primaries != "bt2020" || space != "bt2020nc")
                return new(VideoLoopColorKind.Hdr, false,
                    "HDR-to-SDR requires BT.2020 primaries and a BT.2020 non-constant matrix. Source metadata is missing or inconsistent.");
            // PQ is absolute. HLG uses zimg's 1000-nit reference display. Missing peak
            // metadata uses 1000 nits; never guess PQ from bit depth or missing transfer.
            var hasPqPeak = transfer == "smpte2084" && probe.VideoPeakLuminanceNits is >= 100 and <= 10000;
            var peak = hasPqPeak ? probe.VideoPeakLuminanceNits!.Value : 1000;
            var inputRange = range is "pc" or "full" ? "full" : "limited";
            return new(VideoLoopColorKind.Hdr, true,
                $"HDR source → SDR output (BT.709). {(transfer == "smpte2084" ? "PQ" : "HLG")} tone mapping; reference peak {peak:0} nits" +
                (!hasPqPeak ? " (fallback/reference display)." : "."))
            {
                Filter = $"zscale=pin=bt2020:tin={transfer}:min=bt2020nc:rin={inputRange}:t=linear:npl=100," +
                    // tonemap uses colorspace for luma coefficients even on RGB pixels.
                    // Leaving zscale's GBR matrix would sum R+G+B during desaturation.
                    "format=gbrpf32le,zscale=p=bt709,setparams=colorspace=bt709," +
                    $"tonemap=tonemap=hable:desat=2:peak={VideoLoopFrameReader.Number(peak / 100)}," +
                    "zscale=t=bt709:m=bt709:r=limited:d=error_diffusion,format=yuv444p," + SdrTail
            };
        }

        if (space is not ("" or "bt709" or "fcc" or "bt470bg" or "smpte170m" or "smpte240m" or "gbr" or "bt2020nc" or "bt2020c"))
            return new(VideoLoopColorKind.Undetermined, false,
                "Source color matrix is unsupported or conflicts with the SDR profile. Check the source color metadata before loop export.");

        var wideGamut = primaries is not ("" or "bt709" or "bt470m" or "bt470bg" or "smpte170m" or "smpte240m") ||
            space is "bt2020nc" or "bt2020c" || transfer is "bt2020-10" or "bt2020-12";
        if (transfer is "bt709" or "iec61966-2-1" or "smpte170m" or "smpte240m" or
            "gamma22" or "gamma28" or "bt470m" or "bt470bg" or "bt2020-10" or "bt2020-12")
        {
            if (wideGamut && (primaries != "bt2020" || space != "bt2020nc"))
                return new(VideoLoopColorKind.Sdr, false, "SDR wide-gamut source metadata is incomplete or unsupported; BT.2020 primaries and non-constant matrix are required.");
            // Keep the established ordinary SDR path; explicitly tagged BT.709 and
            // wide-gamut SDR use the same range/matrix conversion as HDR, without tonemap.
            if (wideGamut || transfer == "bt709" && (primaries is "" or "bt709") && (space is "" or "bt709"))
                return new(VideoLoopColorKind.Sdr, true, wideGamut
                    ? "SDR wide-gamut source → SDR BT.709 output (no HDR tone mapping)."
                    : "SDR source. Loop output: MP4/H.264 SDR BT.709.")
                {
                    Filter = $"zscale=pin={(wideGamut ? "bt2020" : "bt709")}:tin={transfer}:min={(wideGamut ? "bt2020nc" : "bt709")}:" +
                        $"rin={(range is "pc" or "full" ? "full" : "limited")}:p=bt709:t=bt709:m=bt709:r=limited:d=error_diffusion,format=yuv444p," + SdrTail
                };
            return new(VideoLoopColorKind.Sdr, true, "SDR source. Loop output: MP4/H.264.");
        }

        // Missing high-bit-depth metadata is ambiguous, rather than evidence of HDR.
        // A planar subsampling name such as yuv410p must not be read as ten-bit.
        var format = Normalize(probe.VideoPixelFormat);
        var profile = Normalize(probe.VideoProfile);
        var highBitDepth = probe.VideoBitDepth > 8 || profile.Contains("main 10", StringComparison.Ordinal) ||
            profile.Contains("main10", StringComparison.Ordinal) || Regex.IsMatch(format,
                @"(?:p|gray)(?:0?10|0?12|0?16|14)(?:le|be)?$|(?:rgb|bgr)48(?:le|be)?$|(?:rgba|bgra)64(?:le|be)?$|(?:p|gray)f32(?:le|be)?$");
        if (transfer.Length > 0 || highBitDepth || wideGamut)
            return new(VideoLoopColorKind.Undetermined, false,
                "Source color transfer is missing or unsupported. SDR/HDR cannot be determined safely for loop export; check the source color metadata.");

        // Preserve the ordinary untagged SDR workflow. Do not call this confirmed SDR.
        return new(VideoLoopColorKind.Undetermined, true,
            "Color transfer unspecified; using the SDR compatibility path for loop output.");
    }

    public void RequireSupported()
    {
        if (!CanRender) throw new InvalidOperationException(Message);
    }

    public async Task RequireAvailableAsync(FfmpegRunner runner, string executable, CancellationToken token)
    {
        RequireSupported();
        token.ThrowIfCancellationRequested();
        if (!ConvertsToSdr) return;
        var file = new FileInfo(executable);
        var key = $"{file.FullName}|{file.Length}|{file.LastWriteTimeUtc.Ticks}";
        await CapabilityGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (!CapabilityCache.TryGetValue(key, out var filters))
            {
                var result = await runner.RunCaptureAsync(executable, ["-hide_banner", "-filters"], token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (!result.Success) throw new InvalidOperationException("Unable to verify FFmpeg color conversion filters. Loop conversion is unavailable.");
                filters = Regex.Matches(result.StandardOutput, @"(?m)^\s*[TSC.]{2,3}\s+(\w+)\s+")
                    .Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
                CapabilityCache[key] = filters;
            }
            RequireFilters(filters);
        }
        finally { CapabilityGate.Release(); }
    }

    internal void RequireFilters(IReadOnlySet<string> filters)
    {
        foreach (var name in ToneMapsHdr ? new[] { "zscale", "tonemap", "sidedata", "setparams" } : new[] { "zscale", "sidedata", "setparams" })
            if (!filters.Contains(name)) throw new InvalidOperationException($"FFmpeg color conversion requires the missing '{name}' filter. Use the bundled FFmpeg; loop conversion is unavailable.");
    }

    internal void AddOutputArguments(List<string> arguments)
    {
        if (ConvertsToSdr) arguments.AddRange(["-color_primaries", "bt709", "-color_trc", "bt709", "-colorspace", "bt709", "-color_range", "tv"]);
    }

    private static string Normalize(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant() ?? "";
        return normalized is "unknown" or "unspecified" or "reserved" ? "" : normalized;
    }
}
