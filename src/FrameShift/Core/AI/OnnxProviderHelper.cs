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
                using var opts = new SessionOptions
                {
                    GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                    EnableMemoryPattern = false,
                    ExecutionMode = ExecutionMode.ORT_SEQUENTIAL
                };
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

        using var cpuOpts = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };
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
        L.Get("OnnxProviderHelper_GPU_processing_DirectML_failed_Autom_48c4569b");

    /// <summary>
    /// Returns a clean user-visible message for an ONNX inference error (strips internal paths).
    /// </summary>
    public static string GetCleanUserMessage(Exception ex, string actionName)
    {
        if (ex is OnnxRuntimeException)
            return L.Format("OnnxProviderHelper_0_failed_ONNX_Runtime_error_Check_th_606b985b",actionName);

        return L.Format("OnnxProviderHelper_0_failed_1_bd5cc80f",actionName,ex.Message);
    }
}
