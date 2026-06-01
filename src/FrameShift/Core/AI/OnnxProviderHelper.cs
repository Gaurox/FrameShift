using System;
using FrameShift.Core.Logging;
using Microsoft.ML.OnnxRuntime;

namespace FrameShift.Core.AI;

/// <summary>
/// Lightweight helper for DirectML / CPU session creation and clean error messages.
/// </summary>
internal static class OnnxProviderHelper
{
    /// <summary>
    /// Tries to create an InferenceSession using DirectML.
    /// On failure, falls back to CPU and logs the full technical error.
    /// Returns (session, provider) where provider is "DirectML" or "CPU".
    /// </summary>
    public static (InferenceSession session, string provider) CreateSessionPreferred(
        string modelPath,
        string logPrefix,
        bool forceCpu = false)
    {
        if (!forceCpu)
        {
            try
            {
                var opts = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };
                opts.AppendExecutionProvider_DML();
                AppLogger.LogStatic($"{logPrefix}: using DirectML provider");
                return (new InferenceSession(modelPath, opts), "DirectML");
            }
            catch (Exception ex)
            {
                AppLogger.LogStatic($"{logPrefix}: DirectML failed during session creation, falling back to CPU. {ex}");
            }
        }
        else
        {
            AppLogger.LogStatic($"{logPrefix}: ForceCpu=true — using CPU provider directly");
        }

        var cpuOpts = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };
        AppLogger.LogStatic($"{logPrefix}: using CPU provider");
        return (new InferenceSession(modelPath, cpuOpts), "CPU");
    }

    /// <summary>
    /// Determines whether an exception that occurred during session.Run() looks like a DirectML runtime failure.
    /// </summary>
    public static bool IsLikelyDmlRuntimeFailure(Exception ex) =>
        ex is OnnxRuntimeException ||
        ex.Message.Contains("DmlExecutionProvider", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("DirectML", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Returns a user-visible message for a DirectML inference failure (no internal paths).
    /// </summary>
    public static string GetDmlFallbackUserMessage() =>
        "GPU processing (DirectML) failed. Automatically switched to CPU.";

    /// <summary>
    /// Returns a clean user-visible message for an ONNX inference error (strips internal paths).
    /// </summary>
    public static string GetCleanUserMessage(Exception ex, string actionName)
    {
        if (ex is OnnxRuntimeException)
            return $"{actionName} failed: ONNX Runtime error. Check the diagnostic log for details.";

        return $"{actionName} failed: {ex.Message}";
    }
}
