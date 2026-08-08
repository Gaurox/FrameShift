using System;
using System.IO;
using FrameShift.Core.AI;
using Xunit;

namespace FrameShift.Tests;

[CollectionDefinition(Name)]
public sealed class FrameShiftPreferenceTestCollection : ICollectionFixture<FrameShiftTestSettingsDirectory>
{
    public const string Name = "FrameShift preference tests";
}

public sealed class FrameShiftTestSettingsDirectory : IDisposable
{
    private readonly string? _previousConfigDirectory;

    public FrameShiftTestSettingsDirectory()
    {
        _previousConfigDirectory = Environment.GetEnvironmentVariable(AiModelSettings.ConfigDirectoryOverrideEnvironmentVariable);
        DirectoryPath = Path.Combine(Path.GetTempPath(), $"frameshift-test-settings_{Guid.NewGuid():N}");
        Directory.CreateDirectory(DirectoryPath);
        Environment.SetEnvironmentVariable(AiModelSettings.ConfigDirectoryOverrideEnvironmentVariable, DirectoryPath);
    }

    public string DirectoryPath { get; }

    public void Dispose()
    {
        AiModelStorage.InvalidateCache();
        Environment.SetEnvironmentVariable(AiModelSettings.ConfigDirectoryOverrideEnvironmentVariable, _previousConfigDirectory);

        if (Directory.Exists(DirectoryPath))
        {
            Directory.Delete(DirectoryPath, recursive: true);
        }
    }
}
