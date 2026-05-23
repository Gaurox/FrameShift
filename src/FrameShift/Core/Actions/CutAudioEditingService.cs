using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FrameShift.Core.FFmpeg;

namespace FrameShift.Core.Actions;

public sealed class CutAudioEditingService
{
    private const int DefaultWaveformPointCount = 860;
    private readonly FfmpegRunner _ffmpegRunner;

    public CutAudioEditingService(FfmpegRunner ffmpegRunner)
    {
        _ffmpegRunner = ffmpegRunner;
    }

    public static string CreateTemporaryRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "FrameShiftCutAudio", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    public async Task<string> CreateEditableWorkingCopyAsync(
        string ffmpegPath,
        string inputPath,
        string temporaryRoot,
        CancellationToken cancellationToken)
    {
        var outputPath = Path.Combine(temporaryRoot, $"edit_{Guid.NewGuid():N}.wav");
        var arguments = new[]
        {
            "-hide_banner",
            "-loglevel", "error",
            "-stats_period", "0.25",
            "-progress", "pipe:1",
            "-nostats",
            "-y",
            "-i", inputPath,
            "-vn",
            "-ac", "2",
            "-ar", "44100",
            "-c:a", "pcm_s16le",
            outputPath
        };

        await RunOutputCommandAsync(
            ffmpegPath,
            arguments,
            outputPath,
            inputPath,
            "Cut Audio Prepare",
            cancellationToken).ConfigureAwait(false);

        return outputPath;
    }

    public async Task<double[]> GenerateWaveformPointsAsync(
        string ffmpegPath,
        string sourcePath,
        string temporaryRoot,
        int pointCount,
        CancellationToken cancellationToken)
    {
        if (pointCount < 32)
        {
            pointCount = 32;
        }

        var waveformPath = Path.Combine(temporaryRoot, $"waveform_{Guid.NewGuid():N}.wav");
        var arguments = new[]
        {
            "-hide_banner",
            "-loglevel", "error",
            "-stats_period", "0.25",
            "-progress", "pipe:1",
            "-nostats",
            "-y",
            "-i", sourcePath,
            "-vn",
            "-ac", "1",
            "-ar", "8000",
            "-c:a", "pcm_s16le",
            waveformPath
        };

        try
        {
            await RunOutputCommandAsync(
                ffmpegPath,
                arguments,
                waveformPath,
                sourcePath,
                "Cut Audio Waveform",
                cancellationToken).ConfigureAwait(false);

            return GetWaveformPointsFromWav(waveformPath, pointCount);
        }
        finally
        {
            ConversionActionHelper.DeleteIfExists(waveformPath);
        }
    }

    public async Task<string> RemoveSelectionAsync(
        string ffmpegPath,
        string inputPath,
        string temporaryRoot,
        double startSeconds,
        double endSeconds,
        double currentDurationSeconds,
        CancellationToken cancellationToken)
    {
        if (startSeconds <= 0.0005d && endSeconds >= currentDurationSeconds - 0.0005d)
        {
            throw new InvalidOperationException("Remove selection cannot delete the entire audio.");
        }

        var outputPath = Path.Combine(temporaryRoot, $"edit_{Guid.NewGuid():N}.wav");
        var filter = BuildRemoveFilter(startSeconds, endSeconds, currentDurationSeconds);
        var arguments = new[]
        {
            "-hide_banner",
            "-loglevel", "error",
            "-stats_period", "0.25",
            "-progress", "pipe:1",
            "-nostats",
            "-y",
            "-i", inputPath,
            "-filter_complex", filter,
            "-map", "[out]",
            "-c:a", "pcm_s16le",
            outputPath
        };

        await RunOutputCommandAsync(
            ffmpegPath,
            arguments,
            outputPath,
            inputPath,
            "Cut Audio Remove Selection",
            cancellationToken).ConfigureAwait(false);

        return outputPath;
    }

    public async Task<string> SilenceSelectionAsync(
        string ffmpegPath,
        string inputPath,
        string temporaryRoot,
        double startSeconds,
        double endSeconds,
        double currentDurationSeconds,
        CancellationToken cancellationToken)
    {
        var silenceDuration = endSeconds - startSeconds;
        if (silenceDuration <= 0.0005d)
        {
            throw new InvalidOperationException("Selection is too short to silence.");
        }

        var outputPath = Path.Combine(temporaryRoot, $"edit_{Guid.NewGuid():N}.wav");
        var filter = BuildSilenceFilter(startSeconds, endSeconds, currentDurationSeconds);
        var arguments = new[]
        {
            "-hide_banner",
            "-loglevel", "error",
            "-stats_period", "0.25",
            "-progress", "pipe:1",
            "-nostats",
            "-y",
            "-i", inputPath,
            "-f", "lavfi",
            "-t", FormatSeconds(silenceDuration),
            "-i", "anullsrc=channel_layout=stereo:sample_rate=44100",
            "-filter_complex", filter,
            "-map", "[out]",
            "-c:a", "pcm_s16le",
            outputPath
        };

        await RunOutputCommandAsync(
            ffmpegPath,
            arguments,
            outputPath,
            inputPath,
            "Cut Audio Silence Selection",
            cancellationToken).ConfigureAwait(false);

        return outputPath;
    }

    public async Task<string> CreatePreviewAsync(
        string ffmpegPath,
        string inputPath,
        string temporaryRoot,
        double startSeconds,
        double endSeconds,
        CancellationToken cancellationToken)
    {
        var durationSeconds = endSeconds - startSeconds;
        if (durationSeconds <= 0.05d)
        {
            throw new InvalidOperationException("Selection is too short to preview.");
        }

        var previewPath = Path.Combine(temporaryRoot, $"preview_{Guid.NewGuid():N}.wav");
        var arguments = new[]
        {
            "-hide_banner",
            "-loglevel", "error",
            "-stats_period", "0.25",
            "-progress", "pipe:1",
            "-nostats",
            "-y",
            "-ss", FormatSeconds(startSeconds),
            "-t", FormatSeconds(durationSeconds),
            "-i", inputPath,
            "-vn",
            "-ac", "2",
            "-ar", "44100",
            "-c:a", "pcm_s16le",
            previewPath
        };

        await RunOutputCommandAsync(
            ffmpegPath,
            arguments,
            previewPath,
            inputPath,
            "Cut Audio Preview",
            cancellationToken).ConfigureAwait(false);

        return previewPath;
    }

    public static double[] GetWaveformPointsFromWav(string wavPath, int pointCount = DefaultWaveformPointCount)
    {
        if (pointCount < 32)
        {
            pointCount = 32;
        }

        var bytes = File.ReadAllBytes(wavPath);
        if (bytes.Length < 44)
        {
            throw new InvalidOperationException("Invalid WAV data.");
        }

        var dataOffset = FindDataOffset(bytes);
        var dataLength = bytes.Length - dataOffset;
        if (dataLength <= 0)
        {
            throw new InvalidOperationException("WAV data chunk not found.");
        }

        var sampleCount = (int)Math.Floor(dataLength / 2d);
        if (sampleCount <= 0)
        {
            throw new InvalidOperationException("No audio samples found.");
        }

        var samplesPerBucket = Math.Max(1, (int)Math.Ceiling(sampleCount / (double)pointCount));
        var points = new List<double>(pointCount);

        for (var bucket = 0; bucket < pointCount; bucket++)
        {
            var startSample = bucket * samplesPerBucket;
            if (startSample >= sampleCount)
            {
                points.Add(0d);
                continue;
            }

            var endSample = Math.Min(sampleCount, startSample + samplesPerBucket);
            var peak = 0d;

            for (var sampleIndex = startSample; sampleIndex < endSample; sampleIndex++)
            {
                var offset = dataOffset + (sampleIndex * 2);
                var sample = BitConverter.ToInt16(bytes, offset);
                var amplitude = Math.Abs(sample / 32768d);
                if (amplitude > peak)
                {
                    peak = amplitude;
                }
            }

            points.Add(peak);
        }

        return points.ToArray();
    }

    private async Task RunOutputCommandAsync(
        string ffmpegPath,
        IReadOnlyCollection<string> arguments,
        string outputPath,
        string currentFile,
        string currentAction,
        CancellationToken cancellationToken)
    {
        ConversionActionHelper.DeleteIfExists(outputPath);

        var result = await _ffmpegRunner.RunAsync(
            ffmpegPath,
            arguments,
            null,
            null,
            currentFile,
            currentAction,
            "CPU",
            cancellationToken).ConfigureAwait(false);

        if (result.Canceled)
        {
            ConversionActionHelper.DeleteIfExists(outputPath);
            throw new OperationCanceledException(cancellationToken);
        }

        if (result.ExitCode != 0 || !File.Exists(outputPath))
        {
            ConversionActionHelper.DeleteIfExists(outputPath);
            throw new InvalidOperationException(ConversionActionHelper.GetFriendlyFfmpegError(result.StandardError, "FFmpeg failed during Cut Audio processing."));
        }
    }

    private static string BuildRemoveFilter(double startSeconds, double endSeconds, double currentDurationSeconds)
    {
        if (startSeconds <= 0.0005d)
        {
            return $"[0:a]atrim=start={FormatSeconds(endSeconds)},asetpts=PTS-STARTPTS[out]";
        }

        if (endSeconds >= currentDurationSeconds - 0.0005d)
        {
            return $"[0:a]atrim=0:{FormatSeconds(startSeconds)},asetpts=PTS-STARTPTS[out]";
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"[0:a]atrim=0:{FormatSeconds(startSeconds)},asetpts=PTS-STARTPTS[a0];[0:a]atrim=start={FormatSeconds(endSeconds)},asetpts=PTS-STARTPTS[a1];[a0][a1]concat=n=2:v=0:a=1[out]");
    }

    private static string BuildSilenceFilter(double startSeconds, double endSeconds, double currentDurationSeconds)
    {
        if (startSeconds <= 0.0005d && endSeconds >= currentDurationSeconds - 0.0005d)
        {
            return "[1:a]asetpts=PTS-STARTPTS[out]";
        }

        if (startSeconds <= 0.0005d)
        {
            return $"[1:a]asetpts=PTS-STARTPTS[s];[0:a]atrim=start={FormatSeconds(endSeconds)},asetpts=PTS-STARTPTS[a1];[s][a1]concat=n=2:v=0:a=1[out]";
        }

        if (endSeconds >= currentDurationSeconds - 0.0005d)
        {
            return $"[0:a]atrim=0:{FormatSeconds(startSeconds)},asetpts=PTS-STARTPTS[a0];[1:a]asetpts=PTS-STARTPTS[s];[a0][s]concat=n=2:v=0:a=1[out]";
        }

        return $"[0:a]atrim=0:{FormatSeconds(startSeconds)},asetpts=PTS-STARTPTS[a0];[1:a]asetpts=PTS-STARTPTS[s];[0:a]atrim=start={FormatSeconds(endSeconds)},asetpts=PTS-STARTPTS[a1];[a0][s][a1]concat=n=3:v=0:a=1[out]";
    }

    private static int FindDataOffset(byte[] bytes)
    {
        var dataOffset = 44;
        for (var index = 12; index <= bytes.Length - 8;)
        {
            var chunkId = Encoding.ASCII.GetString(bytes, index, 4);
            var chunkSize = BitConverter.ToInt32(bytes, index + 4);
            if (string.Equals(chunkId, "data", StringComparison.Ordinal))
            {
                return index + 8;
            }

            index += 8 + chunkSize;
            if (chunkSize % 2 != 0)
            {
                index++;
            }
        }

        return dataOffset;
    }

    private static string FormatSeconds(double seconds)
    {
        return seconds.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
