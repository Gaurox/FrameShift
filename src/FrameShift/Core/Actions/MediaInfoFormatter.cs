using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using FrameShift.Core.FFprobe;

namespace FrameShift.Core.Actions;

public enum MediaKind { Video, Audio, Image }

public sealed record MediaInfoReport(string Text, MediaKind Kind);

public static class MediaInfoFormatter
{
    private static readonly string[] ImageExtensions =
        [".jpg", ".jpeg", ".png", ".bmp", ".webp", ".gif", ".tif", ".tiff"];

    public static MediaInfoReport Build(MediaInfoData data, FileInfo fileInfo)
    {
        var ext = fileInfo.Extension.ToLowerInvariant();
        MediaKind kind;

        if (Array.IndexOf(ImageExtensions, ext) >= 0)
            kind = MediaKind.Image;
        else if (data.VideoStreams.Count > 0)
            kind = MediaKind.Video;
        else if (data.AudioStreams.Count > 0)
            kind = MediaKind.Audio;
        else
            kind = MediaKind.Image;

        var text = kind switch
        {
            MediaKind.Video => BuildVideoText(data, fileInfo),
            MediaKind.Audio => BuildAudioText(data, fileInfo),
            _ => BuildImageText(data, fileInfo)
        };

        return new MediaInfoReport(text, kind);
    }

    private static string BuildVideoText(MediaInfoData data, FileInfo fileInfo)
    {
        var lines = new List<string>();
        var fileSize = FormatSize(fileInfo.Length);
        var duration = FormatDuration(data.Duration);
        var globalBitrate = FormatBitrate(data.BitRate);

        string formatName;
        if (!string.IsNullOrWhiteSpace(data.FormatName) && data.FormatName.Contains("mp4", StringComparison.OrdinalIgnoreCase))
            formatName = "MPEG-4";
        else if (!string.IsNullOrWhiteSpace(data.FormatLongName))
            formatName = data.FormatLongName;
        else if (!string.IsNullOrWhiteSpace(data.FormatName))
            formatName = data.FormatName;
        else
            formatName = "-";

        var isMp4 = !string.IsNullOrWhiteSpace(data.FormatName) &&
                    data.FormatName.Contains("mp4", StringComparison.OrdinalIgnoreCase);
        var formatProfile = isMp4 ? "Base Media / Version 2" : "-";
        var codecId = isMp4 ? "mp42 (isom/mp42)" : "-";
        var encodedDate = data.TagCreationTime ?? "-";

        var vStream = data.VideoStreams.Count > 0 ? data.VideoStreams[0] : null;

        lines.Add("Général");
        lines.Add(AddLine("Nom complet", fileInfo.FullName));
        lines.Add(AddLine("Format", formatName));
        lines.Add(AddLine("Format, Profil", formatProfile));
        lines.Add(AddLine("Identifiant du codec", codecId));
        lines.Add(AddLine("Taille du fichier", fileSize));
        lines.Add(AddLine("Durée", duration));
        lines.Add(AddLine("Type de débit global", "Variable"));
        lines.Add(AddLine("Débit global", globalBitrate));

        if (vStream != null)
            lines.Add(AddLine("Débit im/s", FormatFps(vStream.AvgFrameRate)));

        lines.Add(AddLine("Date d'encodage", encodedDate));
        lines.Add(AddLine("Date de marquage", encodedDate));
        lines.Add(string.Empty);

        if (vStream != null)
        {
            var vFormat = GetVideoFormat(vStream.CodecName);
            var vFormatInfo = GetVideoFormatInfo(vStream.CodecName);
            var vBitrate = FormatBitrate(vStream.BitRate);
            var vFps = FormatFps(vStream.AvgFrameRate);
            var width = vStream.Width.HasValue ? $"{vStream.Width.Value} pixels" : "-";
            var height = vStream.Height.HasValue ? $"{vStream.Height.Value} pixels" : "-";
            var chroma = GetChroma(vStream.PixFmt);
            var bitDepth = GetBitDepth(vStream.BitsPerRawSample, vStream.PixFmt);
            var codecTag = vStream.CodecTagString ?? "-";
            var vEncodedDate = vStream.TagCreationTime ?? "-";

            lines.Add("Vidéo");
            lines.Add(AddLine("ID", vStream.Index.ToString()));
            lines.Add(AddLine("Format", vFormat));
            lines.Add(AddLine("Format/Infos", vFormatInfo));
            lines.Add(AddLine("Format, Profil", vStream.Profile ?? "-"));
            lines.Add(AddLine("Identifiant du codec", codecTag));
            lines.Add(AddLine("Identifiant du codec/Infos", vFormatInfo));
            lines.Add(AddLine("Durée", duration));
            lines.Add(AddLine("Débit", vBitrate));
            lines.Add(AddLine("Largeur", width));
            lines.Add(AddLine("Hauteur", height));
            lines.Add(AddLine("Facteur de forme l/h", vStream.DisplayAspectRatio ?? "-"));
            lines.Add(AddLine("Type de débit im/s", "Constant"));
            lines.Add(AddLine("Débit im/s", vFps));
            lines.Add(AddLine("Espace de couleurs", vStream.PixFmt ?? "-"));
            lines.Add(AddLine("Sous-échantillonnage de la chrominance", chroma));
            lines.Add(AddLine("Profondeur binaire", bitDepth));
            lines.Add(AddLine("Bits/(Pixel*Image)", "-"));
            lines.Add(AddLine("Taille du flux", "-"));
            lines.Add(AddLine("Bibliothèque utilisée", vStream.TagEncoder ?? "-"));
            lines.Add(AddLine("Langue", vStream.TagLanguage ?? "-"));
            lines.Add(AddLine("Date d'encodage", vEncodedDate));
            lines.Add(AddLine("Date de marquage", vEncodedDate));
            lines.Add(AddLine("Gamme de couleurs", vStream.ColorRange ?? "-"));
            lines.Add(AddLine("Configuration des codecs", codecTag));
            lines.Add(string.Empty);
        }

        if (data.AudioStreams.Count > 0)
        {
            var aStream = data.AudioStreams[0];
            var aFormat = GetAudioFormat(aStream.CodecName);
            var aFormatInfo = GetAudioFormatInfo(aStream.CodecName);
            var aBitrate = FormatBitrate(aStream.BitRate);
            var channels = FormatChannels(aStream.Channels);
            var channelLayout = !string.IsNullOrWhiteSpace(aStream.ChannelLayout)
                ? aStream.ChannelLayout.ToUpperInvariant() : "-";
            var sampleRate = FormatSampleRate(aStream.SampleRate);
            var frameRate = FormatAudioFrameRate(aStream.CodecName, aStream.SampleRate);
            var codecTagA = aStream.CodecTagString ?? "-";
            var aEncodedDate = aStream.TagCreationTime ?? "-";

            lines.Add("Audio");
            lines.Add(AddLine("ID", aStream.Index.ToString()));
            lines.Add(AddLine("Format", aFormat));
            lines.Add(AddLine("Format/Infos", aFormatInfo));
            lines.Add(AddLine("Identifiant du codec", codecTagA));
            lines.Add(AddLine("Durée", duration));
            lines.Add(AddLine("Type de débit", "Variable"));
            lines.Add(AddLine("Débit", aBitrate));
            lines.Add(AddLine("Débit maximum", "-"));
            lines.Add(AddLine("Canal(aux)", channels));
            lines.Add(AddLine("Agencement des canaux", channelLayout));
            lines.Add(AddLine("Echantillonnage", sampleRate));
            lines.Add(AddLine("Débit im/s", frameRate));
            lines.Add(AddLine("Mode de compression", GetCompressionMode(aStream.CodecName)));
            lines.Add(AddLine("Taille du flux", "-"));
            lines.Add(AddLine("Langue", aStream.TagLanguage ?? "-"));
            lines.Add(AddLine("Date d'encodage", aEncodedDate));
            lines.Add(AddLine("Date de marquage", aEncodedDate));
        }

        return string.Join("\r\n", lines);
    }

    private static string BuildAudioText(MediaInfoData data, FileInfo fileInfo)
    {
        var lines = new List<string>();
        var fileSize = FormatSize(fileInfo.Length);
        var duration = FormatDuration(data.Duration);
        var globalBitrate = FormatBitrate(data.BitRate);
        var library = data.TagEncoder ?? "-";

        var aStream = data.AudioStreams.Count > 0 ? data.AudioStreams[0] : null;
        var format = GetAudioFormat(aStream?.CodecName);
        var version = "-";
        var profile = "-";
        var bitrate = globalBitrate;
        var channels = "-";
        var sampleRate = "-";
        var frameRate = "-";
        var compression = "-";

        if (aStream != null)
        {
            if (!string.IsNullOrWhiteSpace(aStream.BitRate))
                bitrate = FormatBitrate(aStream.BitRate);

            channels = FormatChannels(aStream.Channels);
            sampleRate = FormatSampleRate(aStream.SampleRate);
            frameRate = FormatAudioFrameRate(aStream.CodecName, aStream.SampleRate);
            compression = GetCompressionMode(aStream.CodecName);

            if (int.TryParse(aStream.SampleRate, out var sr))
            {
                if (string.Equals(aStream.CodecName, "mp3", StringComparison.OrdinalIgnoreCase))
                {
                    version = sr <= 24000 ? "Version 2" : "Version 1";
                    profile = "Layer 3";
                }
                else if (string.Equals(aStream.CodecName, "aac", StringComparison.OrdinalIgnoreCase))
                {
                    profile = "LC";
                }
            }
        }

        lines.Add("Général");
        lines.Add(AddLine("Nom complet", fileInfo.FullName));
        lines.Add(AddLine("Format", format));
        lines.Add(AddLine("Taille du fichier", fileSize));
        lines.Add(AddLine("Durée", duration));
        lines.Add(AddLine("Type de débit global", "Variable"));
        lines.Add(AddLine("Débit global", globalBitrate));
        lines.Add(AddLine("Bibliothèque utilisée", library));
        lines.Add(string.Empty);
        lines.Add("Audio");
        lines.Add(AddLine("Format", format));
        lines.Add(AddLine("Format, Version", version));
        lines.Add(AddLine("Format, Profil", profile));
        lines.Add(AddLine("Durée", duration));
        lines.Add(AddLine("Type de débit", "Variable"));
        lines.Add(AddLine("Débit", bitrate));
        lines.Add(AddLine("Canal(aux)", channels));
        lines.Add(AddLine("Echantillonnage", sampleRate));
        lines.Add(AddLine("Débit im/s", frameRate));
        lines.Add(AddLine("Mode de compression", compression));
        lines.Add(AddLine("Taille du flux", $"{fileSize} (100%)"));

        return string.Join("\r\n", lines);
    }

    private static string BuildImageText(MediaInfoData data, FileInfo fileInfo)
    {
        var lines = new List<string>();
        var fileSize = FormatSize(fileInfo.Length);
        var vStream = data.VideoStreams.Count > 0 ? data.VideoStreams[0] : null;

        var format = GetVideoFormat(vStream?.CodecName);
        var width = "-";
        var height = "-";
        var colorSpace = "-";
        var bitDepth = "-";
        var compression = GetCompressionMode(vStream?.CodecName);

        if (vStream != null)
        {
            if (vStream.Width.HasValue)
                width = $"{vStream.Width.Value} pixels";

            if (vStream.Height.HasValue)
                height = $"{vStream.Height.Value} pixels";

            if (!string.IsNullOrWhiteSpace(vStream.ColorSpace))
                colorSpace = vStream.ColorSpace.ToUpperInvariant();
            else if (!string.IsNullOrWhiteSpace(vStream.PixFmt))
                colorSpace = vStream.PixFmt;

            bitDepth = GetBitDepth(vStream.BitsPerRawSample, vStream.PixFmt);
        }

        lines.Add("Général");
        lines.Add(AddLine("Nom complet", fileInfo.FullName));
        lines.Add(AddLine("Nom du fichier", fileInfo.Name));
        lines.Add(AddLine("Format", format));
        lines.Add(AddLine("Taille du fichier", fileSize));
        lines.Add(string.Empty);
        lines.Add("Image");
        lines.Add(AddLine("Format", format));
        lines.Add(AddLine("Largeur", width));
        lines.Add(AddLine("Hauteur", height));
        lines.Add(AddLine("Espace de couleurs", colorSpace));
        lines.Add(AddLine("Profondeur binaire", bitDepth));
        lines.Add(AddLine("Mode de compression", compression));
        lines.Add(AddLine("Taille du flux", $"{fileSize} (100%)"));

        return string.Join("\r\n", lines);
    }

    private static string FormatSize(long bytes)
    {
        const long GiB = 1L << 30;
        const long MiB = 1 << 20;
        const long KiB = 1 << 10;
        if (bytes >= GiB) return $"{(double)bytes / GiB:N2} Gio";
        if (bytes >= MiB) return $"{(double)bytes / MiB:N0} Mio";
        if (bytes >= KiB) return $"{(double)bytes / KiB:N0} Kio";
        return $"{bytes} octets";
    }

    private static string FormatDuration(string? seconds)
    {
        if (string.IsNullOrWhiteSpace(seconds)) return "-";
        if (!double.TryParse(seconds, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || value <= 0)
            return "-";

        var ts = TimeSpan.FromSeconds(value);
        if (ts.Hours > 0) return $"{ts.Hours} h {ts.Minutes:D2} min {ts.Seconds:D2} s";
        if (ts.Minutes > 0) return $"{ts.Minutes} min {ts.Seconds:D2} s";
        return $"{ts.Seconds} s {ts.Milliseconds:D3} ms";
    }

    private static string FormatBitrate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "-";
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || number <= 0)
            return "-";

        var kbps = number / 1000;
        return kbps >= 1000
            ? $"{kbps / 1000:N1} Mb/s"
            : $"{kbps:N0} kb/s";
    }

    private static string FormatFps(string? rate)
    {
        if (string.IsNullOrWhiteSpace(rate) || rate == "0/0") return "-";

        var parts = rate.Split('/');
        if (parts.Length != 2) return rate;
        if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var num)) return "-";
        if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var den) || den == 0) return "-";

        return $"{num / den:N3} im/s";
    }

    private static string FormatSampleRate(string? sampleRateStr)
    {
        if (!int.TryParse(sampleRateStr, out var sr) || sr <= 0) return "-";
        return $"{sr / 1000.0:N1} kHz";
    }

    private static string FormatAudioFrameRate(string? codecName, string? sampleRateStr)
    {
        if (!int.TryParse(sampleRateStr, out var sr) || sr <= 0) return "-";

        var spf = 1024;
        if (string.Equals(codecName, "mp3", StringComparison.OrdinalIgnoreCase))
            spf = sr <= 24000 ? 576 : 1152;

        return $"{(double)sr / spf:N3} im/s ({spf} SPF)";
    }

    private static string FormatChannels(string? channelsStr)
    {
        if (!int.TryParse(channelsStr, out var ch)) return "-";
        return ch == 1 ? "1 canal" : ch == 2 ? "2 canaux" : $"{ch} canaux";
    }

    private static string AddLine(string label, string? value)
    {
        var v = string.IsNullOrWhiteSpace(value) ? "-" : value;
        return $"{label,-42}: {v}";
    }

    private static string GetVideoFormat(string? codecName)
    {
        if (string.IsNullOrWhiteSpace(codecName)) return "-";
        return codecName.ToLowerInvariant() switch
        {
            "h264" => "AVC",
            "hevc" => "HEVC",
            "mpeg4" => "MPEG-4 Visual",
            "vp9" => "VP9",
            "av1" => "AV1",
            "mjpeg" => "JPEG",
            _ => codecName.ToUpperInvariant()
        };
    }

    private static string GetVideoFormatInfo(string? codecName)
    {
        if (string.IsNullOrWhiteSpace(codecName)) return "-";
        return codecName.ToLowerInvariant() switch
        {
            "h264" => "Advanced Video Coding",
            "hevc" => "High Efficiency Video Coding",
            "mpeg4" => "MPEG-4 Visual",
            "vp9" => "Google VP9",
            "av1" => "AOMedia Video 1",
            _ => "-"
        };
    }

    private static string GetAudioFormat(string? codecName)
    {
        if (string.IsNullOrWhiteSpace(codecName)) return "-";
        return codecName.ToLowerInvariant() switch
        {
            "aac" => "AAC LC",
            "mp3" => "MPEG Audio",
            "flac" => "FLAC",
            "opus" => "Opus",
            "vorbis" => "Vorbis",
            "pcm_s16le" => "PCM",
            _ => codecName.ToUpperInvariant()
        };
    }

    private static string GetAudioFormatInfo(string? codecName)
    {
        if (string.IsNullOrWhiteSpace(codecName)) return "-";
        return codecName.ToLowerInvariant() switch
        {
            "aac" => "Advanced Audio Codec Low Complexity",
            "mp3" => "MPEG Audio",
            "flac" => "Free Lossless Audio Codec",
            "opus" => "Opus",
            "vorbis" => "Vorbis",
            _ => "-"
        };
    }

    private static string GetCompressionMode(string? codecName)
    {
        if (string.IsNullOrWhiteSpace(codecName)) return "-";
        return codecName.ToLowerInvariant() switch
        {
            "flac" or "pcm_s16le" or "wav" or "png" => "Sans perte",
            "bmp" => "Non compressé",
            "tiff" => "Variable",
            _ => "Avec perte"
        };
    }

    private static string GetChroma(string? pixFmt)
    {
        if (string.IsNullOrWhiteSpace(pixFmt)) return "-";
        if (pixFmt.Contains("420")) return "4:2:0";
        if (pixFmt.Contains("422")) return "4:2:2";
        if (pixFmt.Contains("444")) return "4:4:4";
        return "-";
    }

    private static string GetBitDepth(string? bitsPerRawSample, string? pixFmt)
    {
        if (!string.IsNullOrWhiteSpace(bitsPerRawSample) && bitsPerRawSample != "0")
            return $"{bitsPerRawSample} bits";

        if (!string.IsNullOrWhiteSpace(pixFmt))
        {
            if (pixFmt.Contains("10")) return "10 bits";
            if (pixFmt.Contains("yuv") || pixFmt.Contains("rgb") || pixFmt.Contains("gray"))
                return "8 bits";
        }

        return "-";
    }
}
