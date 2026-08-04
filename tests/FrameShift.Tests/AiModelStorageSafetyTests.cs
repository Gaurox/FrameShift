using System;
using System.IO;
using FrameShift.Core.AI;
using Xunit;

namespace FrameShift.Tests;

public sealed class AiModelStorageSafetyTests
{
    [Fact]
    public void CustomModelsDirectory_RejectsDangerousRootsAndAllowsDedicatedFolder()
    {
        var defaultDirectory = AiModelSettings.DefaultModelsDirectoryPath;
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var programFilesDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var dedicatedDirectory = Path.Combine(Path.GetTempPath(), $"frameshift models é_{Guid.NewGuid():N}");

        Assert.False(AiModelDirectorySafety.TryNormalizeCustomDirectory(Path.GetPathRoot(defaultDirectory), out _));
        Assert.False(AiModelDirectorySafety.TryNormalizeCustomDirectory(userProfile, out _));
        Assert.False(AiModelDirectorySafety.TryNormalizeCustomDirectory(windowsDirectory, out _));
        Assert.False(AiModelDirectorySafety.TryNormalizeCustomDirectory(programFilesDirectory, out _));
        Assert.False(AiModelDirectorySafety.TryNormalizeCustomDirectory(AppContext.BaseDirectory, out _));
        Assert.False(AiModelDirectorySafety.TryNormalizeCustomDirectory(Path.GetDirectoryName(userProfile), out _));

        Assert.True(AiModelDirectorySafety.TryNormalizeCustomDirectory(dedicatedDirectory, out var normalized));
        Assert.Equal(Path.GetFullPath(dedicatedDirectory), normalized, ignoreCase: true);
    }

    [Fact]
    public void SharedModelsRoot_SelectsOnlyMarkedKnownDirectoriesForUninstall()
    {
        var sharedRoot = CreateTempDirectory("frameshift shared models é");
        try
        {
            var foreignFile = Path.Combine(sharedRoot, "family-photo.txt");
            var foreignKnownDirectory = Path.Combine(sharedRoot, "upscale-image-onnx");
            var ownedDirectory = Path.Combine(sharedRoot, "rife");
            var ownedDirectoryWithForeignFile = Path.Combine(sharedRoot, "htdemucs");
            File.WriteAllText(foreignFile, "must remain");
            Directory.CreateDirectory(foreignKnownDirectory);
            File.WriteAllText(Path.Combine(foreignKnownDirectory, "other-tool.onnx"), "must remain");

            AiModelStorage.EnsureDirectory(sharedRoot, ownedDirectory);
            File.WriteAllText(Path.Combine(ownedDirectory, "rife_v426_x2.onnx"), "FrameShift model");
            AiModelStorage.EnsureDirectory(sharedRoot, ownedDirectoryWithForeignFile);
            File.WriteAllText(Path.Combine(ownedDirectoryWithForeignFile, "foreign-model.onnx"), "must remain");

            var ownedDirectories = AiModelStorage.GetOwnedKnownModelDirectories(sharedRoot);

            Assert.Single(ownedDirectories);
            Assert.Equal(ownedDirectory, ownedDirectories[0], ignoreCase: true);
            Assert.False(AiModelStorage.HasOwnershipMarker(foreignKnownDirectory));
            Assert.False(AiModelStorage.CanDeleteOwnedKnownModelDirectory(sharedRoot, ownedDirectoryWithForeignFile));

            // This models the exact deletion scope used by the installer: only the selected
            // owned directory is removed; the shared root and all neighbouring content remain.
            Directory.Delete(ownedDirectories[0], recursive: true);

            Assert.True(File.Exists(foreignFile));
            Assert.True(File.Exists(Path.Combine(foreignKnownDirectory, "other-tool.onnx")));
            Assert.True(File.Exists(Path.Combine(ownedDirectoryWithForeignFile, "foreign-model.onnx")));
            Assert.True(Directory.Exists(sharedRoot));
        }
        finally
        {
            Directory.Delete(sharedRoot, recursive: true);
        }
    }

    [Fact]
    public void ExistingDirectory_IsNeverRetroactivelyMarkedAsFrameShiftOwned()
    {
        var root = CreateTempDirectory("frameshift existing models");
        try
        {
            var existingDirectory = Path.Combine(root, "rife");
            Directory.CreateDirectory(existingDirectory);
            File.WriteAllText(Path.Combine(existingDirectory, "other-tool.onnx"), "foreign");

            AiModelStorage.EnsureDirectory(root, existingDirectory);

            Assert.False(AiModelStorage.HasOwnershipMarker(existingDirectory));
            Assert.Empty(AiModelStorage.GetOwnedKnownModelDirectories(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Installer_UninstallCodeNeverDeletesModelsRootOrRecursivelyDeletesModelDirectories()
    {
        var installerPath = FindRepositoryFile(Path.Combine("installer", "FrameShift.iss"));
        var installerSource = File.ReadAllText(installerPath);

        Assert.Contains("procedure DeleteOwnedModelDirectories", installerSource, StringComparison.Ordinal);
        Assert.Contains("CanDeleteOwnedModelDirectory(ModelsDir, Candidate, RelativePath)", installerSource, StringComparison.Ordinal);
        Assert.Contains("ContainsOnlyExpectedModelFiles", installerSource, StringComparison.Ordinal);
        Assert.Contains("HasReparsePointInPath", installerSource, StringComparison.Ordinal);
        Assert.Contains("ModelDirectoryMarkerFileName", installerSource, StringComparison.Ordinal);
        Assert.Contains("DeleteOwnedModelDirectoryFiles", installerSource, StringComparison.Ordinal);
        Assert.Contains("DeleteFile(FilePath)", installerSource, StringComparison.Ordinal);
        Assert.Contains("RemoveDir(DirectoryName)", installerSource, StringComparison.Ordinal);
        Assert.False(installerSource.Contains("DelTree(ModelsDir", StringComparison.OrdinalIgnoreCase));
        Assert.False(installerSource.Contains("DelTree(Candidate", StringComparison.OrdinalIgnoreCase));

        foreach (var relativePath in AiModelStorage.KnownModelDirectoryRelativePaths)
        {
            Assert.Contains(relativePath, installerSource, StringComparison.Ordinal);
        }
    }

    private static string CreateTempDirectory(string prefix)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{prefix}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static string FindRepositoryFile(string relativePath)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"Could not find repository file '{relativePath}'.");
    }
}
