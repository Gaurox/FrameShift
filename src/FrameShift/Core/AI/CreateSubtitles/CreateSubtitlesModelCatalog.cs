using System;
using System.Collections.Generic;
using System.Linq;

namespace FrameShift.Core.AI.CreateSubtitles;

internal sealed record CreateSubtitlesModelArtifact(
    string FileName,
    string DownloadUrl,
    string ExpectedSha256,
    long ExpectedSizeBytes);

internal sealed record CreateSubtitlesModelDefinition(
    string Id,
    string DisplayName,
    string Folder,
    string License,
    IReadOnlyList<CreateSubtitlesModelArtifact> Artifacts,
    int FeatureDim = 80)
{
    public long ExpectedTotalSizeBytes => Artifacts.Sum(static artifact => artifact.ExpectedSizeBytes);
}

internal static class CreateSubtitlesModelCatalog
{
    private const string HfBase = "https://huggingface.co/Gaurox/frameshift-models/resolve/main";
    private const string License = "MIT (OpenAI Whisper) + Apache-2.0 notice (sherpa-onnx export/runtime tooling)";

    private static readonly CreateSubtitlesModelDefinition[] s_models =
    [
        new(
            "whisper-base",
            "Whisper Base — Fast",
            "whisper-base-onnx",
            License,
            [
                new CreateSubtitlesModelArtifact(
                    "base-encoder.onnx",
                    $"{HfBase}/whisper-base-onnx/base-encoder.onnx",
                    "460A643843ED71310B766BE9C4D193226EF4DD15C7AAB4966397D32B362BA3DF",
                    95_088_280L),
                new CreateSubtitlesModelArtifact(
                    "base-decoder.onnx",
                    $"{HfBase}/whisper-base-onnx/base-decoder.onnx",
                    "F48EC999D771F4AC115D66DB719C1CE7572D067F543392C230A3F4747927FD95",
                    196_625_767L),
                new CreateSubtitlesModelArtifact(
                    "base-tokens.txt",
                    $"{HfBase}/whisper-base-onnx/base-tokens.txt",
                    "FEBEED8E568F92D9CA984580BC2E6B605B867DC5BA4486F9646DE381B44A8226",
                    866_987L)
            ]),

        new(
            "whisper-small",
            "Whisper Small — Recommended",
            "whisper-small-onnx",
            License,
            [
                new CreateSubtitlesModelArtifact(
                    "small-encoder.onnx",
                    $"{HfBase}/whisper-small-onnx/small-encoder.onnx",
                    "71D9D6C67DEE3FD8C1CD4CC84F1D556740FA67060E419CE5AFE1F6F6F9B5242B",
                    409_531_196L),
                new CreateSubtitlesModelArtifact(
                    "small-decoder.onnx",
                    $"{HfBase}/whisper-small-onnx/small-decoder.onnx",
                    "CF7CA341970FCA7EEFEFA17EB5CCE4FC79AE10B86539F550C72E85065790D528",
                    559_275_550L),
                new CreateSubtitlesModelArtifact(
                    "small-tokens.txt",
                    $"{HfBase}/whisper-small-onnx/small-tokens.txt",
                    "FEBEED8E568F92D9CA984580BC2E6B605B867DC5BA4486F9646DE381B44A8226",
                    866_987L)
            ]),

        new(
            "whisper-turbo",
            "Whisper Large-v3 Turbo — Quality",
            "whisper-large-v3-turbo-onnx",
            License,
            Artifacts: [
                // Artifacts[0..2] are passed to the sherpa-onnx worker (encoder, decoder, tokens).
                // Artifacts[3] (encoder.weights) must reside in the same directory so ONNX Runtime
                // resolves the encoder's external data automatically.
                new CreateSubtitlesModelArtifact(
                    "turbo-encoder.onnx",
                    $"{HfBase}/whisper-large-v3-turbo-onnx/turbo-encoder.onnx",
                    "9B64665BB77AEE767AD43E829886120D6A55CB6EBD8D99335A5591ACBC35D648",
                    736_099L),
                new CreateSubtitlesModelArtifact(
                    "turbo-decoder.onnx",
                    $"{HfBase}/whisper-large-v3-turbo-onnx/turbo-decoder.onnx",
                    "84E8BD8D811E7F63F5779DB1B0E0D28C425B00D8CE761ACB132B8F72836AE956",
                    636_260_783L),
                new CreateSubtitlesModelArtifact(
                    "turbo-tokens.txt",
                    $"{HfBase}/whisper-large-v3-turbo-onnx/turbo-tokens.txt",
                    "FEBEED8E568F92D9CA984580BC2E6B605B867DC5BA4486F9646DE381B44A8226",
                    866_987L),
                new CreateSubtitlesModelArtifact(
                    "turbo-encoder.weights",
                    $"{HfBase}/whisper-large-v3-turbo-onnx/turbo-encoder.weights",
                    "746F879ECF066450AB0CDECC05383380B85157270FF6C0A9FB7CFDD917036E12",
                    2_600_325_120L)
            ],
            FeatureDim: 128)
    ];

    public static IReadOnlyList<CreateSubtitlesModelDefinition> GetAll() => s_models;

    public static CreateSubtitlesModelDefinition GetDefault() =>
        s_models.First(static m => m.Id == "whisper-small");

    public static CreateSubtitlesModelDefinition? GetById(string? modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return null;
        }

        return s_models.FirstOrDefault(model =>
            string.Equals(model.Id, modelId, StringComparison.OrdinalIgnoreCase));
    }
}
