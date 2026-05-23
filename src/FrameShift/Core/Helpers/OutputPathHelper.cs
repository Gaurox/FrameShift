using System;
using System.IO;
using FrameShift.Core.Actions;

namespace FrameShift.Core.Helpers;

public static class OutputPathHelper
{
    public static string CreateUniqueOutputPath(string inputPath, string suffix, string? extension = null)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            throw new ArgumentException("Input path is required.", nameof(inputPath));
        }

        var directory = Path.GetDirectoryName(inputPath);
        var fileName = Path.GetFileNameWithoutExtension(inputPath);
        var finalExtension = string.IsNullOrWhiteSpace(extension) ? Path.GetExtension(inputPath) : extension;
        var basePath = Path.Combine(directory ?? string.Empty, $"{fileName}{suffix}");
        var candidatePath = $"{basePath}{finalExtension}";

        if (!File.Exists(candidatePath))
        {
            return candidatePath;
        }

        for (var index = 1; index < 1000; index++)
        {
            candidatePath = $"{basePath}_{index:000}{finalExtension}";
            if (!File.Exists(candidatePath))
            {
                return candidatePath;
            }
        }

        throw new IOException(MediaActionMessages.UniqueOutputPathExhaustedErrorKey);
    }

    public static string CreateUniqueOutputDirectoryPath(string inputPath, string suffix)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            throw new ArgumentException("Input path is required.", nameof(inputPath));
        }

        if (string.IsNullOrWhiteSpace(suffix))
        {
            throw new ArgumentException("Suffix is required.", nameof(suffix));
        }

        var directory = Path.GetDirectoryName(inputPath);
        var fileName = Path.GetFileNameWithoutExtension(inputPath);
        var desiredPath = Path.Combine(directory ?? string.Empty, $"{fileName}{suffix}");
        return CreateUniqueDirectoryPath(desiredPath);
    }

    public static string CreateUniqueDirectoryPath(string desiredPath)
    {
        if (string.IsNullOrWhiteSpace(desiredPath))
        {
            throw new ArgumentException("Desired path is required.", nameof(desiredPath));
        }

        if (!PathExists(desiredPath))
        {
            return desiredPath;
        }

        for (var index = 1; index < 1000; index++)
        {
            var candidatePath = $"{desiredPath}_{index:000}";
            if (!PathExists(candidatePath))
            {
                return candidatePath;
            }
        }

        throw new IOException(MediaActionMessages.UniqueOutputPathExhaustedErrorKey);
    }

    private static bool PathExists(string path)
    {
        return File.Exists(path) || Directory.Exists(path);
    }
}
