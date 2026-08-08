using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace FrameShift.Tests;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class CreateSubtitlesIntegrationFactAttribute : FactAttribute
{
    public CreateSubtitlesIntegrationFactAttribute(
        string sampleFileName,
        string modelDirectory,
        params string[] modelFileNames)
    {
        Skip = GetSkipReason(sampleFileName, modelDirectory, modelFileNames);
    }

    private static string? GetSkipReason(
        string sampleFileName,
        string modelDirectory,
        IReadOnlyList<string> modelFileNames)
    {
        var repositoryRoot = GetRepositoryRoot();
        if (repositoryRoot is null)
        {
            return "Create Subtitles integration test requires the FrameShift repository root.";
        }

        var scratchRoot = Path.Combine(repositoryRoot, "scratch", "WhisperBaseOnnxSpike");
        var samplePath = Path.Combine(scratchRoot, "samples", sampleFileName);
        var modelRoot = Path.Combine(scratchRoot, modelDirectory);
        var missingAssets = new List<string>();

        if (!File.Exists(samplePath))
        {
            missingAssets.Add(samplePath);
        }

        foreach (var modelFileName in modelFileNames)
        {
            var modelPath = Path.Combine(modelRoot, modelFileName);
            if (!File.Exists(modelPath))
            {
                missingAssets.Add(modelPath);
            }
        }

        return missingAssets.Count == 0
            ? null
            : $"Create Subtitles integration test requires local scratch assets: {string.Join(", ", missingAssets)}";
    }

    private static string? GetRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "src", "FrameShift", "FrameShift.csproj")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }
}
