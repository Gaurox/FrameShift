using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace FrameShift.Tests;

public sealed class ShellMenuPayloadTests
{
    [Theory]
    [InlineData("valid", true)]
    [InlineData("runtime", false)]
    [InlineData("hash", false)]
    [InlineData("managed", false)]
    [InlineData("x86", false)]
    [InlineData("probe", false)]
    [InlineData("extra", false)]
    public void PayloadVerifier_RequiresServicedNativeBinaryAndSuccessfulProbe(string scenario, bool expectedSuccess)
    {
        var directory = Path.Combine(Path.GetTempPath(), "FrameShift native payload " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            // A header-only PE fixture is sufficient for the packaging verifier. The separate
            // COM probe loads the real compiled DLL and verifies exports and native behavior.
            var bytes = new byte[512];
            BitConverter.GetBytes((ushort)0x5A4D).CopyTo(bytes, 0);
            BitConverter.GetBytes(128).CopyTo(bytes, 0x3C);
            BitConverter.GetBytes(0x4550u).CopyTo(bytes, 128);
            BitConverter.GetBytes((ushort)(scenario == "x86" ? 0x014C : 0x8664)).CopyTo(bytes, 132);
            BitConverter.GetBytes((ushort)0x2000).CopyTo(bytes, 150);
            BitConverter.GetBytes((ushort)0x20B).CopyTo(bytes, 152);
            if (scenario == "managed") BitConverter.GetBytes(1ul).CopyTo(bytes, 152 + 112 + 14 * 8);
            var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            var component = Path.Combine(directory, "Shell", hash);
            Directory.CreateDirectory(component);
            File.WriteAllBytes(Path.Combine(component, "FrameShift.ShellMenu.dll"), bytes);
            File.WriteAllText(Path.Combine(component, "component.json"), JsonSerializer.Serialize(new
            {
                SchemaVersion = 1, Version = "1.23.0", BuildId = hash, DllSha256 = scenario == "hash" ? new string('f', 64) : hash,
                SdkVersion = "8.0.427", AotCompilerVersion = scenario == "runtime" ? "8.0.26" : "8.0.33",
                MenuSchemaVersion = 2, Clsid = "{F6740A5A-42BC-4716-9512-531F0B254862}",
                NativeProbePassed = scenario == "probe" ? 0 : 23
            }));
            if (scenario == "extra") File.WriteAllText(Path.Combine(component, "FrameShift.ShellMenu.Fixture.dll"), "must not ship");
            var source = FindRepositoryRoot();
            var script = Path.Combine(directory, "verify.ps1");
            File.WriteAllText(script, "param([string]$Helper,[string]$Payload)\n$ErrorActionPreference='Stop'\nSet-StrictMode -Version Latest\n. $Helper\nAssert-ShellMenuPayload -PublishDirectory $Payload -Version '1.23.0'\n");
            var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"))
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in new[] { "-NoProfile", "-File", script, "-Helper", Path.Combine(source, "tools/build/ShellMenu.ps1"), "-Payload", directory }) start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            Assert.True(process.WaitForExit(30_000));
            Assert.True((process.ExitCode == 0) == expectedSuccess, stdout + stderr);
        }
        finally { Directory.Delete(directory, true); }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "build_installer.ps1"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}
