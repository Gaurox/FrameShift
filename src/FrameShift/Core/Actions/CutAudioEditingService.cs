using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Buffers.Binary;
using System.Threading;
using System.Threading.Tasks;
using FrameShift.Core.FFmpeg;
using NAudio.Wave;

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

            return GetWaveformPointsFromWav(waveformPath, pointCount, cancellationToken);
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
            throw new InvalidOperationException(L.Get("CutAudioEditingService_Remove_selection_cannot_delete_the_e_77702c9d"));
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
            throw new InvalidOperationException(L.Get("CutAudioEditingService_Selection_is_too_short_to_silence_2474fe02"));
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
            throw new InvalidOperationException(L.Get("CutAudioEditingService_Selection_is_too_short_to_preview_8e9a0f99"));
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

    public static double[] GetWaveformPointsFromWav(
        string wavPath, int pointCount = DefaultWaveformPointCount, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var stream = File.OpenRead(wavPath);
        return ReadWaveform(stream, pointCount, cancellationToken);
    }

    internal static double[] ReadWaveform(Stream stream, int pointCount, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var reader = new WaveFileReader(stream);
        if (reader.WaveFormat.Encoding != WaveFormatEncoding.Pcm ||
            reader.WaveFormat.BitsPerSample != 16 || reader.WaveFormat.Channels != 1 || reader.Length % 2 != 0)
            throw new InvalidDataException("Expected a mono PCM 16-bit WAV for the waveform.");

        var sampleCount = reader.Length / 2;
        if (sampleCount == 0) throw new InvalidDataException(L.Get("CutAudioEditingService_No_audio_samples_found_7177388c"));
        pointCount = Math.Max(32, pointCount);
        var samplesPerBucket = Math.Max(1, (sampleCount + pointCount - 1) / pointCount);
        var points = new double[pointCount];
        var buffer = new byte[16 * 1024];
        long sampleIndex = 0;
        while (sampleIndex < sampleCount)
        {
            token.ThrowIfCancellationRequested();
            var requested = (int)Math.Min(buffer.Length, (sampleCount - sampleIndex) * 2);
            var read = reader.Read(buffer, 0, requested);
            if (read == 0 || read % 2 != 0) throw new InvalidDataException("Truncated WAV samples.");
            for (var offset = 0; offset < read; offset += 2, sampleIndex++)
            {
                var sample = BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(offset, 2));
                var bucket = (int)(sampleIndex / samplesPerBucket);
                points[bucket] = Math.Max(points[bucket], Math.Abs(sample / 32768d));
            }
        }
        token.ThrowIfCancellationRequested();
        return points;
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
            if (!result.ProcessTerminationConfirmed)
            {
                throw new TimeoutException(L.Get("CutAudioEditingService_FFmpeg_did_not_confirm_process_termi_b062c283"));
            }

            ConversionActionHelper.DeleteIfExists(outputPath);
            throw new OperationCanceledException(cancellationToken);
        }

        if (result.ExitCode != 0 || !File.Exists(outputPath))
        {
            ConversionActionHelper.DeleteIfExists(outputPath);
            throw new InvalidOperationException(ConversionActionHelper.GetFriendlyFfmpegError(result.StandardError, L.Get("CutAudioEditingService_FFmpeg_failed_during_Cut_Audio_proce_4d68ed5c")));
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

    private static string FormatSeconds(double seconds)
    {
        return seconds.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
