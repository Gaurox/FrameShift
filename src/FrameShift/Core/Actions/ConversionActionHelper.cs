using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;

namespace FrameShift.Core.Actions;

public static class ConversionActionHelper
{
    public static string GetOption(ActionRequest request, string key, string defaultValue)
    {
        if (request.Options is null)
        {
            return defaultValue;
        }

        foreach (var option in request.Options)
        {
            if (string.Equals(option.Key, key, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(option.Value))
            {
                return option.Value;
            }
        }

        return defaultValue;
    }

    public static void DeleteIfExists(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        const int maxAttempts = 10;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                return;
            }
            catch (IOException) when (attempt < maxAttempts)
            {
                Thread.Sleep(200);
            }
            catch (UnauthorizedAccessException) when (attempt < maxAttempts)
            {
                Thread.Sleep(200);
            }
        }
    }

    public static string GetFriendlyFfmpegError(string stderr, string fallbackMessage)
    {
        if (string.IsNullOrWhiteSpace(stderr))
        {
            return fallbackMessage;
        }

        var lower = stderr.ToLowerInvariant();
        if (lower.Contains("no space left") || lower.Contains("disk full"))
        {
            return MediaActionMessages.DiskFull();
        }

        if (lower.Contains("permission denied") || lower.Contains("access is denied") || lower.Contains("cannot open output file"))
        {
            return MediaActionMessages.OutputWriteFailed();
        }

        if (lower.Contains("invalid data found") || lower.Contains("corrupt"))
        {
            return MediaActionMessages.FileCorrupted();
        }

        if (lower.Contains("no jpeg data found in image") ||
            lower.Contains("error submitting packet to decoder") ||
            lower.Contains("cannot determine format of input") ||
            lower.Contains("decode error rate") ||
            lower.Contains("nothing was written into output file") ||
            lower.Contains("could not open encoder before eof"))
        {
            return MediaActionMessages.FileCorrupted();
        }

        if (lower.Contains("stream map") || lower.Contains("no streams to mux") || lower.Contains("does not contain any stream") || lower.Contains("cannot find a matching stream"))
        {
            return "The source file does not contain a stream that FFmpeg can convert.";
        }

        if (lower.Contains("invalid channel layout") ||
            lower.Contains("error while opening encoder") ||
            lower.Contains("unsupported channel layout"))
        {
            return "The selected output audio settings are not compatible with this source file.";
        }

        if (lower.Contains("invalid argument") || lower.Contains("error initializing filter") || lower.Contains("failed to configure input pad"))
        {
            return "The selected output settings are not compatible with this source file.";
        }

        if (lower.Contains("unsupported") || lower.Contains("not found") || lower.Contains("unknown encoder") || lower.Contains("unknown decoder"))
        {
            return MediaActionMessages.CodecUnsupported();
        }

        var lines = stderr.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.Length > 0 ? string.Join(Environment.NewLine, lines.TakeLast(3)) : fallbackMessage;
    }

    public static string GetFriendlyProbeError(string stderr)
    {
        if (string.IsNullOrWhiteSpace(stderr))
        {
            return MediaActionMessages.ProbeFailed();
        }

        var lower = stderr.ToLowerInvariant();
        if (lower.Contains("invalid data found") ||
            lower.Contains("corrupt") ||
            lower.Contains("moov atom not found") ||
            lower.Contains("no jpeg data found in image") ||
            lower.Contains("unexpected eof") ||
            lower.Contains("end of file") ||
            lower.Contains("truncated") ||
            lower.Contains("could not find codec parameters"))
        {
            return MediaActionMessages.FileCorrupted();
        }

        return MediaActionMessages.ProbeFailed();
    }

    public static string GetFriendlyExceptionMessage(Exception exception, string fallbackMessage)
    {
        if (exception is OperationCanceledException)
        {
            return fallbackMessage;
        }

        if (exception is FileNotFoundException fileNotFound &&
            (!string.IsNullOrWhiteSpace(fileNotFound.FileName) || fileNotFound.Message.Contains("Tools\\ffmpeg", StringComparison.OrdinalIgnoreCase)))
        {
            var toolName = Path.GetFileNameWithoutExtension(fileNotFound.FileName);
            if (string.IsNullOrWhiteSpace(toolName))
            {
                toolName = fileNotFound.Message.Contains("ffprobe", StringComparison.OrdinalIgnoreCase)
                    ? "ffprobe"
                    : "ffmpeg";
            }

            return MediaActionMessages.RequiredToolMissing(toolName);
        }

        if (exception is ArgumentException argumentException &&
            string.Equals(argumentException.ParamName, "inputPath", StringComparison.OrdinalIgnoreCase))
        {
            return MediaActionMessages.InputPathRequired();
        }

        if (exception is IOException ioException &&
            string.Equals(ioException.Message, MediaActionMessages.UniqueOutputPathExhaustedErrorKey, StringComparison.Ordinal))
        {
            return MediaActionMessages.UniqueOutputPathUnavailable();
        }

        if (exception is Win32Exception win32Exception &&
            win32Exception.NativeErrorCode == 112)
        {
            return MediaActionMessages.DiskFull();
        }

        return string.IsNullOrWhiteSpace(exception.Message)
            ? fallbackMessage
            : exception.Message;
    }
}
