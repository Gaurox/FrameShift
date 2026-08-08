using System;
using System.Runtime.InteropServices;
using NAudio.Wave;

namespace FrameShift.Core.AI.RemoveNoise;

/// <summary>
/// Estimates the managed working set used by the full-sequence DeepFilterNet path.
/// The model still requires the complete spectral sequence, so this is a preflight,
/// not an artificial duration limit.
/// </summary>
internal readonly record struct RemoveNoiseMemoryEstimate(
    long SampleCount,
    long FrameCount,
    long EstimatedWorkingSetBytes,
    long RequiredAvailableBytes,
    long LegacyEstimatedWorkingSetBytes);

internal static class RemoveNoiseMemoryEstimator
{
    private const int SampleRate = 48_000;
    private const int FftSize = 960;
    private const int HopSize = 480;
    private const int SpectrumBins = FftSize / 2 + 1;
    private const int ErbBins = 32;
    private const int DfBins = 96;
    private const int DfCoefficientValues = 10;
    private const long FloatBytes = sizeof(float);

    // Covers the three ONNX sessions and runtime workspaces not visible in managed arrays.
    // It is a reserve, not a duration cap: the main estimate remains proportional to input length.
    private const long OnnxSessionReserveBytes = 256L * 1024 * 1024;
    private const long SafetyNumerator = 5;
    private const long SafetyDenominator = 4;

    public static bool TryEstimate(TimeSpan duration, out RemoveNoiseMemoryEstimate estimate, out string failureMessage)
    {
        estimate = default;
        failureMessage = string.Empty;

        if (duration <= TimeSpan.Zero)
        {
            failureMessage = "Remove Noise could not determine a valid audio duration.";
            return false;
        }

        try
        {
            var sampleCount = checked((long)Math.Ceiling(duration.TotalSeconds * SampleRate));
            var frameCount = checked((sampleCount + FftSize + HopSize - 1) / HopSize);
            if (sampleCount > int.MaxValue || frameCount > int.MaxValue)
            {
                failureMessage = "This audio is too long for the current full-sequence Remove Noise engine.";
                return false;
            }

            long analyzedSpectra = BytesFor(frameCount, 2L * SpectrumBins);
            long modelFeatures = BytesFor(frameCount, ErbBins + 2L * DfBins);
            long decoderOutputs = BytesFor(frameCount, ErbBins + (long)DfBins * DfCoefficientValues);
            long cleanAudio = BytesFor(sampleCount, 1);
            long sourceAudio = BytesFor(sampleCount, 1);

            long optimizedAnalysis = checked(sourceAudio + analyzedSpectra + modelFeatures);
            long optimizedInference = checked(analyzedSpectra + modelFeatures + decoderOutputs + cleanAudio);
            long optimized = Math.Max(optimizedAnalysis, optimizedInference);

            // The old path also retained an input copy, source feature arrays, copied encoder
            // outputs, processed spectra and a second full synthesized waveform.
            long legacyExtra = checked(
                sourceAudio +
                modelFeatures +
                modelFeatures +
                decoderOutputs +
                analyzedSpectra +
                BytesFor(frameCount, HopSize));
            long legacy = checked(optimized + legacyExtra);

            long required = checked(ScaleUp(optimized, SafetyNumerator, SafetyDenominator) + OnnxSessionReserveBytes);
            estimate = new RemoveNoiseMemoryEstimate(sampleCount, frameCount, optimized, required, legacy);
            return true;
        }
        catch (OverflowException)
        {
            failureMessage = "This audio is too long for the current full-sequence Remove Noise engine.";
            return false;
        }
    }

    public static bool HasEnoughAvailableMemory(RemoveNoiseMemoryEstimate estimate, long availablePhysicalMemoryBytes) =>
        availablePhysicalMemoryBytes >= estimate.RequiredAvailableBytes;

    public static bool TryValidateAvailableMemory(TimeSpan duration, out string failureMessage)
    {
        if (!TryEstimate(duration, out var estimate, out failureMessage))
            return false;

        if (!TryGetAvailablePhysicalMemory(out var availablePhysicalMemoryBytes))
            return true; // Do not reject a valid job merely because Windows did not expose a snapshot.

        if (HasEnoughAvailableMemory(estimate, availablePhysicalMemoryBytes))
            return true;

        failureMessage =
            $"Remove Noise needs about {FormatBytes(estimate.RequiredAvailableBytes)} of currently available RAM for this duration, " +
            $"but only {FormatBytes(availablePhysicalMemoryBytes)} is available. Close other applications or choose a shorter file.";
        return false;
    }

    public static bool TryValidateAudioFile(string inputPath, out string failureMessage)
    {
        try
        {
            using var reader = new AudioFileReader(inputPath);
            return TryValidateAvailableMemory(reader.TotalTime, out failureMessage);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException or NotSupportedException)
        {
            failureMessage = "Remove Noise could not determine the audio duration for its memory preflight.";
            return false;
        }
    }

    internal static string FormatBytes(long bytes)
    {
        const double unit = 1024d * 1024d;
        return $"{bytes / unit:0.0} MB";
    }

    private static long BytesFor(long count, long floatsPerItem) => checked(count * floatsPerItem * FloatBytes);

    private static long ScaleUp(long value, long numerator, long denominator) =>
        checked((value * numerator + denominator - 1) / denominator);

    private static bool TryGetAvailablePhysicalMemory(out long availablePhysicalMemoryBytes)
    {
        availablePhysicalMemoryBytes = 0;
        try
        {
            var memoryStatus = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
            if (!GlobalMemoryStatusEx(ref memoryStatus) || memoryStatus.AvailPhys > long.MaxValue)
                return false;

            availablePhysicalMemoryBytes = (long)memoryStatus.AvailPhys;
            return true;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);
}
