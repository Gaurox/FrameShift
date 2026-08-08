using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Xunit;

namespace FrameShift.Tests;

public sealed class ReleaseScriptTests
{
    [Fact]
    public void CanonicalRelease_CleanFixtureRestoresBeforeNoRestoreTests_CleansPublishAndBuildsFromCurrentPayload()
    {
        using var fixture = new ReleaseFixture();
        fixture.CreatePreviousPublishResidue();

        Assert.False(Directory.Exists(Path.Combine(fixture.Root, "obj")));

        var result = fixture.Run();

        Assert.True(
            result.ExitCode == 0,
            $"Canonical release failed unexpectedly.{Environment.NewLine}stdout:{Environment.NewLine}{result.StandardOutput}{Environment.NewLine}stderr:{Environment.NewLine}{result.StandardError}{Environment.NewLine}commands:{Environment.NewLine}{result.Log}");
        Assert.False(File.Exists(fixture.PreviousPublishResiduePath));
        Assert.True(File.Exists(fixture.InstallerPath));
        Assert.Contains($"/DPublishOutputDir={fixture.PublishDirectory}", result.Log, StringComparison.OrdinalIgnoreCase);

        var restoreLines = result.LogLines
            .Where(line => line.StartsWith("dotnet restore ", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Assert.Equal(3, restoreLines.Length);
        Assert.All(restoreLines, line => Assert.Contains("--locked-mode", line, StringComparison.OrdinalIgnoreCase));

        var testIndex = Array.FindIndex(result.LogLines, line => line.StartsWith("dotnet test ", StringComparison.OrdinalIgnoreCase));
        var publishIndex = Array.FindIndex(result.LogLines, line => line.StartsWith("dotnet publish ", StringComparison.OrdinalIgnoreCase));
        var lastRestoreIndex = Array.FindLastIndex(result.LogLines, line => line.StartsWith("dotnet restore ", StringComparison.OrdinalIgnoreCase));
        Assert.True(testIndex > lastRestoreIndex, "Release tests must run after all locked restores.");
        Assert.True(publishIndex > testIndex, "Publish must run only after Release tests pass.");
        Assert.Contains("--no-restore", result.LogLines[testIndex], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--no-restore", result.LogLines[publishIndex], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CanonicalRelease_TestFailureStopsBeforePublish()
    {
        using var fixture = new ReleaseFixture { FailingStep = "test" };

        var result = fixture.Run();

        Assert.NotEqual(0, result.ExitCode);
        Assert.DoesNotContain(result.LogLines, line => line.StartsWith("dotnet publish ", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.LogLines, line => line.StartsWith("iscc ", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CanonicalRelease_PublishFailureStopsBeforeInno()
    {
        using var fixture = new ReleaseFixture { FailingStep = "publish" };

        var result = fixture.Run();

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(result.LogLines, line => line.StartsWith("dotnet publish ", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.LogLines, line => line.StartsWith("iscc ", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CanonicalRelease_InnoFailureReturnsNonZero()
    {
        using var fixture = new ReleaseFixture { FailingStep = "inno" };

        var result = fixture.Run();

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(result.LogLines, line => line.StartsWith("iscc ", StringComparison.OrdinalIgnoreCase));
        Assert.False(File.Exists(fixture.InstallerPath));
    }

    private sealed class ReleaseFixture : IDisposable
    {
        private readonly string _toolsDirectory;
        private readonly string _logPath;
        private readonly string _fakeIsccPath;

        public ReleaseFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), $"frameshift-release-script-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Root);
            _toolsDirectory = Path.Combine(Root, "tools");
            _logPath = Path.Combine(Root, "release-command.log");
            _fakeIsccPath = Path.Combine(_toolsDirectory, "fake-iscc.cmd");
            PublishDirectory = Path.Combine(Root, "publish", "FrameShift-win-x64");
            InstallerPath = Path.Combine(Root, "installer", "FrameShift_1.2.3_Setup.exe");
            PreviousPublishResiduePath = Path.Combine(PublishDirectory, "stale-from-previous-publish.txt");

            CreateRepositoryFixture();
            CreateFakeTools();
        }

        public string Root { get; }
        public string PublishDirectory { get; }
        public string InstallerPath { get; }
        public string PreviousPublishResiduePath { get; }
        public string? FailingStep { get; init; }

        public void CreatePreviousPublishResidue()
        {
            Directory.CreateDirectory(PublishDirectory);
            File.WriteAllText(PreviousPublishResiduePath, "old publish residue");
        }

        public ReleaseResult Run()
        {
            var startInfo = new ProcessStartInfo(GetPowerShellPath())
            {
                WorkingDirectory = Root,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-ExecutionPolicy");
            startInfo.ArgumentList.Add("Bypass");
            startInfo.ArgumentList.Add("-File");
            startInfo.ArgumentList.Add(Path.Combine(Root, "build_installer.ps1"));
            startInfo.ArgumentList.Add("-AllowDirty");
            startInfo.ArgumentList.Add("-IsccPath");
            startInfo.ArgumentList.Add(_fakeIsccPath);
            startInfo.Environment["PATH"] = $"{_toolsDirectory};{Environment.GetEnvironmentVariable("PATH")}";
            startInfo.Environment["FRAMESHIFT_RELEASE_TEST_LOG"] = _logPath;
            startInfo.Environment["FRAMESHIFT_RELEASE_TEST_PUBLISH_DIR"] = PublishDirectory;
            startInfo.Environment["FRAMESHIFT_RELEASE_TEST_INSTALLER"] = InstallerPath;
            startInfo.Environment["FRAMESHIFT_RELEASE_TEST_FAIL_STEP"] = FailingStep ?? string.Empty;

            using var process = Process.Start(startInfo);
            Assert.NotNull(process);
            var standardOutput = process.StandardOutput.ReadToEnd();
            var standardError = process.StandardError.ReadToEnd();
            Assert.True(process.WaitForExit(30_000), "The release script test process timed out.");

            var logLines = File.Exists(_logPath) ? File.ReadAllLines(_logPath) : Array.Empty<string>();
            return new ReleaseResult(process.ExitCode, standardOutput, standardError, logLines);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }

        private void CreateRepositoryFixture()
        {
            File.Copy(FindRepositoryFile("build_installer.ps1"), Path.Combine(Root, "build_installer.ps1"));
            WriteFile("src\\FrameShift\\FrameShift.csproj", "<Project><PropertyGroup><Version>1.2.3</Version></PropertyGroup></Project>");
            WriteFile("src\\FrameShift.SubtitlesWorker\\FrameShift.SubtitlesWorker.csproj", "<Project />");
            WriteFile("tests\\FrameShift.Tests\\FrameShift.Tests.csproj", "<Project />");
            WriteFile("docs\\CHANGELOG.md", "## 1.2.3");
            WriteFile("installer\\FrameShift.iss", "; fixture");
        }

        private void CreateFakeTools()
        {
            Directory.CreateDirectory(_toolsDirectory);
            WriteBatchFile("git.cmd", new[]
            {
                "@echo off",
                "echo git %*>> \"%FRAMESHIFT_RELEASE_TEST_LOG%\"",
                "exit /b 0"
            });
            WriteBatchFile("dotnet.cmd", new[]
            {
                "@echo off",
                "echo dotnet %*>> \"%FRAMESHIFT_RELEASE_TEST_LOG%\"",
                "if /I \"%1\"==\"restore\" exit /b 0",
                "if /I \"%1\"==\"test\" goto test",
                "if /I \"%1\"==\"publish\" goto publish",
                "exit /b 2",
                ":test",
                "if /I \"%FRAMESHIFT_RELEASE_TEST_FAIL_STEP%\"==\"test\" exit /b 31",
                "exit /b 0",
                ":publish",
                "if /I \"%FRAMESHIFT_RELEASE_TEST_FAIL_STEP%\"==\"publish\" exit /b 32",
                "mkdir \"%FRAMESHIFT_RELEASE_TEST_PUBLISH_DIR%\\Tools\\ffmpeg\" 2>nul",
                "mkdir \"%FRAMESHIFT_RELEASE_TEST_PUBLISH_DIR%\\Workers\\CreateSubtitlesWorker\" 2>nul",
                "type nul > \"%FRAMESHIFT_RELEASE_TEST_PUBLISH_DIR%\\FrameShift.exe\"",
                "type nul > \"%FRAMESHIFT_RELEASE_TEST_PUBLISH_DIR%\\Tools\\ffmpeg\\ffmpeg.exe\"",
                "type nul > \"%FRAMESHIFT_RELEASE_TEST_PUBLISH_DIR%\\Tools\\ffmpeg\\ffprobe.exe\"",
                "type nul > \"%FRAMESHIFT_RELEASE_TEST_PUBLISH_DIR%\\Workers\\CreateSubtitlesWorker\\FrameShift.SubtitlesWorker.exe\"",
                "exit /b 0"
            });
            WriteBatchFile("fake-iscc.cmd", new[]
            {
                "@echo off",
                "echo iscc %*>> \"%FRAMESHIFT_RELEASE_TEST_LOG%\"",
                "if /I \"%FRAMESHIFT_RELEASE_TEST_FAIL_STEP%\"==\"inno\" exit /b 41",
                "if exist \"%FRAMESHIFT_RELEASE_TEST_PUBLISH_DIR%\\stale-from-previous-publish.txt\" exit /b 42",
                "if not exist \"%FRAMESHIFT_RELEASE_TEST_PUBLISH_DIR%\\FrameShift.exe\" exit /b 43",
                "if not exist \"%FRAMESHIFT_RELEASE_TEST_PUBLISH_DIR%\\Tools\\ffmpeg\\ffmpeg.exe\" exit /b 44",
                "echo %* | findstr /C:\"/DPublishOutputDir=%FRAMESHIFT_RELEASE_TEST_PUBLISH_DIR%\" >nul",
                "if errorlevel 1 exit /b 45",
                "echo fixture installer> \"%FRAMESHIFT_RELEASE_TEST_INSTALLER%\"",
                "exit /b 0"
            });
        }

        private void WriteBatchFile(string fileName, IEnumerable<string> lines)
        {
            File.WriteAllLines(Path.Combine(_toolsDirectory, fileName), lines);
        }

        private void WriteFile(string relativePath, string contents)
        {
            var path = Path.Combine(Root, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, contents);
        }

        private static string GetPowerShellPath()
        {
            var path = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            Assert.True(File.Exists(path), $"Windows PowerShell is required for release-script tests: {path}");
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

    private sealed record ReleaseResult(int ExitCode, string StandardOutput, string StandardError, string[] LogLines)
    {
        public string Log => string.Join(Environment.NewLine, LogLines);
    }
}
