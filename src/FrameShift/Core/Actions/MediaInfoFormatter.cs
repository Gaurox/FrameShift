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

        lines.Add(L.Get("MediaInfoFormatter_General_94d95d2c"));
        lines.Add(AddLine(L.Get("MediaInfoFormatter_Complete_name_d8f3518e"), fileInfo.FullName));
        lines.Add(AddLine(L.Get("MediaInfoFormatter_Format_bbaaeb1a"), formatName));
        lines.Add(AddLine(L.Get("MediaInfoFormatter_Format_profile_13165b04"), formatProfile));
        lines.Add(AddLine(L.Get("MediaInfoFormatter_Codec_ID_56b50c8e"), codecId));
        lines.Add(AddLine(L.Get("MediaInfoFormatter_File_size_9dbe4c6c"), fileSize));
        lines.Add(AddLine(L.Get("MediaInfoFormatter_Duration_90e29fac"), duration));
        lines.Add(AddLine(L.Get("MediaInfoFormatter_Overall_bit_rate_mode_321a6960"), L.Get("MediaInfoFormatter_Variable_dc047645")));
        lines.Add(AddLine(L.Get("MediaInfoFormatter_Overall_bit_rate_42cc0643"), globalBitrate));

        if (vStream != null)
            lines.Add(AddLine(L.Get("MediaInfoFormatter_Frame_rate_22210a15"), FormatFps(vStream.AvgFrameRate)));

        lines.Add(AddLine(L.Get("MediaInfoFormatter_Encoded_date_18046f1e"), encodedDate));
        lines.Add(AddLine(L.Get("MediaInfoFormatter_Tagged_date_14214e77"), encodedDate));
        lines.Add(string.Empty);

        if (vStream != null)
        {
            var vFormat = GetVideoFormat(vStream.CodecName);
            var vFormatInfo = GetVideoFormatInfo(vStream.CodecName);
            var vBitrate = FormatBitrate(vStream.BitRate);
            var vFps = FormatFps(vStream.AvgFrameRate);
            var width = vStream.Width.HasValue ? L.Format("MediaInfoFormatter_0_pixels_ee332521",vStream.Width.Value) : "-";
            var height = vStream.Height.HasValue ? L.Format("MediaInfoFormatter_0_pixels_ee332521",vStream.Height.Value) : "-";
            var chroma = GetChroma(vStream.PixFmt);
            var bitDepth = GetBitDepth(vStream.BitsPerRawSample, vStream.PixFmt);
            var codecTag = vStream.CodecTagString ?? "-";
            var vEncodedDate = vStream.TagCreationTime ?? "-";

            lines.Add(L.Get("MediaInfoFormatter_Video_dc664e59"));
            lines.Add(AddLine("ID", vStream.Index.ToString()));
            lines.Add(AddLine(L.Get("MediaInfoFormatter_Format_bbaaeb1a"), vFormat));
            lines.Add(AddLine(L.Get("MediaInfoFormatter_Format_Info_9fbd409b"), vFormatInfo));
            lines.Add(AddLine(L.Get("MediaInfoFormatter_Format_profile_13165b04"), vStream.Profile ?? "-"));
            lines.Add(AddLine(L.Get("MediaInfoFormatter_Codec_ID_56b50c8e"), codecTag));
            lines.Add(AddLine(L.Get("MediaInfoFormatter_Codec_ID_Info_e42e7e8e"), vFormatInfo));
            lines.Add(AddLine(L.Get("MediaInfoFormatter_Duration_90e29fac"), duration));
            lines.Add(AddLine(L.Get("MediaInfoFormatter_Bit_rate_b6ac4cbd"), vBitrate));
            lines.Add(AddLine(L.Get("MediaInfoFormatter_Width_64403ac5"), width));
            lines.Add(AddLine(L.Get("MediaInfoFormatter_Height_1badde1a"), height));
            lines.Add(AddLine(L.Get("MediaInfoFormatter_Display_aspect_ratio_6dadf1e8"), vStream.DisplayAspectRatio ?? "-"));
            lines.Add(AddLine(L.Get("MediaInfoFormatter_Frame_rate_mode_ff9625f6"), L.Get("MediaInfoFormatter_Constant_bd9c6da1")));
            lines.Add(AddLine(L.Get("MediaInfoFormatter_Frame_rate_22210a15"), vFps));
            lines.Add(AddLine(L.Get("MediaInfoFormatter_Color_space_000000cd"), vStream.PixFmt ?? "-"));
            lines.Add(AddLine(L.Get("MediaInfoFormatter_Chroma_subsampling_4f504116"), chroma));
            lines.Add(AddLine(L.Get("MediaInfoFormatter_Bit_depth_2f9035bd"), bitDepth));
            lines.Add(AddLine(L.Get("MediaInfoFormatter_Bits_Pixel_Frame_35be08cb"), "-"));
            lines.Add(AddLine(L.Get("MediaInfoFormatter_Stream_size_3cd8e56c"), "-"));
            lines.Add(AddLine(L.Get("MediaInfoFormatter_Writing_library_a327ba47"), vStream.TagEncoder ?? "-"));
            lines.Add(AddLine(L.Get("MediaInfoFormatter_Language_1200fa47"), vStream.TagLanguage ?? "-"));
            lines.Add(AddLine(L.Get("MediaInfoFormatter_Encoded_date_18046f1e"), vEncodedDate));
            lines.Add(AddLine(L.Get("MediaInfoFormatter_Tagged_date_14214e77"), vEncodedDate));
            lines.Add(AddLine(L.Get("MediaInfoFormatter_Color_range_186b3196"), vStream.ColorRange ?? "-"));
            lines.Add(AddLine(L.Get("MediaInfoFormatter_Codec_configuration_box_0b32b0df"), codecTag));
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

            lines.Add(L.Get("MediaInfoFormatter_Audio_2fd1cbe8"));
            lines.Add(AddLine("ID", aStream.Index.ToString()));
            lines.Add(AddLine(L.Get("MediaInfoFormatter_Format_bbaaeb1a"), aFormat));
            lines.Add(AddLine(L.Get("MediaInfoFormatter_Format_Info_9fbd409b"), aFormatInfo));
            lines.Add(AddLine(L.Get("MediaInfoFormatter_Codec_ID_56b50c8e"), codecTagA));
            lines.Add(AddLine(L.Get("MediaInfoFormatter_Duration_90e29fac"), duration));
            lines.Add(AddLine(L.Get("MediaInfoFormatter_Bit_rate_mode_ba8cdd87"), L.Get("MediaInfoFormatter_Variable_dc047645")));
            lines.Add(AddLine(L.Get("MediaInfoFormatter_Bit_rate_b6ac4cbd"), aBitrate));
            lines.Add(AddLine(L.Get("MediaInfoFormatter_Maximum_bit_rate_63d1295b"), "-"));
            lines.Add(AddLine(L.Get("MediaInfoFormatter_Channel_s_1a4904be"), channels));
            lines.Add(AddLine(L.Get("MediaInfoFormatter_Channel_layout_dc73f627"), channelLayout));
            lines.Add(AddLine(L.Get("MediaInfoFormatter_Sampling_rate_4d469671"), sampleRate));
            lines.Add(AddLine(L.Get("MediaInfoFormatter_Frame_rate_22210a15"), frameRate));
            lines.Add(AddLine(L.Get("MediaInfoFormatter_Compression_mode_fbf48659"), GetCompressionMode(aStream.CodecName)));
            lines.Add(AddLine(L.Get("MediaInfoFormatter_Stream_size_3cd8e56c"), "-"));
            lines.Add(AddLine(L.Get("MediaInfoFormatter_Language_1200fa47"), aStream.TagLanguage ?? "-"));
            lines.Add(AddLine(L.Get("MediaInfoFormatter_Encoded_date_18046f1e"), aEncodedDate));
            lines.Add(AddLine(L.Get("MediaInfoFormatter_Tagged_date_14214e77"), aEncodedDate));
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

        lines.Add(L.Get("MediaInfoFormatter_General_94d95d2c"));
        lines.Add(AddLine(L.Get("MediaInfoFormatter_Complete_name_d8f3518e"), fileInfo.FullName));
        lines.Add(AddLine(L.Get("MediaInfoFormatter_Format_bbaaeb1a"), format));
        lines.Add(AddLine(L.Get("MediaInfoFormatter_File_size_9dbe4c6c"), fileSize));
        lines.Add(AddLine(L.Get("MediaInfoFormatter_Duration_90e29fac"), duration));
        lines.Add(AddLine(L.Get("MediaInfoFormatter_Overall_bit_rate_mode_321a6960"), L.Get("MediaInfoFormatter_Variable_dc047645")));
        lines.Add(AddLine(L.Get("MediaInfoFormatter_Overall_bit_rate_42cc0643"), globalBitrate));
        lines.Add(AddLine(L.Get("MediaInfoFormatter_Writing_library_a327ba47"), library));
        lines.Add(string.Empty);
        lines.Add(L.Get("MediaInfoFormatter_Audio_2fd1cbe8"));
        lines.Add(AddLine(L.Get("MediaInfoFormatter_Format_bbaaeb1a"), format));
        lines.Add(AddLine(L.Get("MediaInfoFormatter_Format_version_eacb86ed"), version));
        lines.Add(AddLine(L.Get("MediaInfoFormatter_Format_profile_13165b04"), profile));
        lines.Add(AddLine(L.Get("MediaInfoFormatter_Duration_90e29fac"), duration));
        lines.Add(AddLine(L.Get("MediaInfoFormatter_Bit_rate_mode_ba8cdd87"), L.Get("MediaInfoFormatter_Variable_dc047645")));
        lines.Add(AddLine(L.Get("MediaInfoFormatter_Bit_rate_b6ac4cbd"), bitrate));
        lines.Add(AddLine(L.Get("MediaInfoFormatter_Channel_s_1a4904be"), channels));
        lines.Add(AddLine(L.Get("MediaInfoFormatter_Sampling_rate_4d469671"), sampleRate));
        lines.Add(AddLine(L.Get("MediaInfoFormatter_Frame_rate_22210a15"), frameRate));
        lines.Add(AddLine(L.Get("MediaInfoFormatter_Compression_mode_fbf48659"), compression));
        lines.Add(AddLine(L.Get("MediaInfoFormatter_Stream_size_3cd8e56c"), $"{fileSize} (100%)"));

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
                width = L.Format("MediaInfoFormatter_0_pixels_ee332521",vStream.Width.Value);

            if (vStream.Height.HasValue)
                height = L.Format("MediaInfoFormatter_0_pixels_ee332521",vStream.Height.Value);

            if (!string.IsNullOrWhiteSpace(vStream.ColorSpace))
                colorSpace = vStream.ColorSpace.ToUpperInvariant();
            else if (!string.IsNullOrWhiteSpace(vStream.PixFmt))
                colorSpace = vStream.PixFmt;

            bitDepth = GetBitDepth(vStream.BitsPerRawSample, vStream.PixFmt);
        }

        lines.Add(L.Get("MediaInfoFormatter_General_94d95d2c"));
        lines.Add(AddLine(L.Get("MediaInfoFormatter_Complete_name_d8f3518e"), fileInfo.FullName));
        lines.Add(AddLine(L.Get("MediaInfoFormatter_File_name_beb6b35b"), fileInfo.Name));
        lines.Add(AddLine(L.Get("MediaInfoFormatter_Format_bbaaeb1a"), format));
        lines.Add(AddLine(L.Get("MediaInfoFormatter_File_size_9dbe4c6c"), fileSize));
        lines.Add(string.Empty);
        lines.Add(L.Get("MediaInfoFormatter_Image_c24ad07a"));
        lines.Add(AddLine(L.Get("MediaInfoFormatter_Format_bbaaeb1a"), format));
        lines.Add(AddLine(L.Get("MediaInfoFormatter_Width_64403ac5"), width));
        lines.Add(AddLine(L.Get("MediaInfoFormatter_Height_1badde1a"), height));
        lines.Add(AddLine(L.Get("MediaInfoFormatter_Color_space_000000cd"), colorSpace));
        lines.Add(AddLine(L.Get("MediaInfoFormatter_Bit_depth_2f9035bd"), bitDepth));
        lines.Add(AddLine(L.Get("MediaInfoFormatter_Compression_mode_fbf48659"), compression));
        lines.Add(AddLine(L.Get("MediaInfoFormatter_Stream_size_3cd8e56c"), $"{fileSize} (100%)"));

        return string.Join("\r\n", lines);
    }

    private static string FormatSize(long bytes)
    {
        const long GiB = 1L << 30;
        const long MiB = 1 << 20;
        const long KiB = 1 << 10;
        if (bytes >= GiB) return $"{(double)bytes / GiB:N2} GiB";
        if (bytes >= MiB) return $"{(double)bytes / MiB:N0} MiB";
        if (bytes >= KiB) return $"{(double)bytes / KiB:N0} KiB";
        return L.Format("MediaInfoFormatter_0_bytes_11ccaf42",bytes);
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

        return $"{num / den:N3} fps";
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

        return $"{(double)sr / spf:N3} fps ({spf} SPF)";
    }

    private static string FormatChannels(string? channelsStr)
    {
        if (!int.TryParse(channelsStr, out var ch)) return "-";
        return ch == 1 ? L.Get("MediaInfoFormatter_1_channel_d203407b") : L.Format("MediaInfoFormatter_0_channels_6975898a",ch);
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
            "flac" or "pcm_s16le" or "wav" or "png" => L.Get("MediaInfoFormatter_Lossless_2bdc3378"),
            "bmp" => L.Get("MediaInfoFormatter_Uncompressed_75903782"),
            "tiff" => L.Get("MediaInfoFormatter_Variable_dc047645"),
            _ => L.Get("MediaInfoFormatter_Lossy_8c2e1cee")
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
            return L.Format("MediaInfoFormatter_0_bits_27c182d5",bitsPerRawSample);

        if (!string.IsNullOrWhiteSpace(pixFmt))
        {
            if (pixFmt.Contains("10")) return L.Get("MediaInfoFormatter_10_bits_de3ce0c5");
            if (pixFmt.Contains("yuv") || pixFmt.Contains("rgb") || pixFmt.Contains("gray"))
                return L.Get("MediaInfoFormatter_8_bits_1852f995");
        }

        return "-";
    }
}
