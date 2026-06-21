using System.IO;

namespace FrameShift.Core.AI.CreateSubtitles;

internal sealed record CreateSubtitlesModelFiles(
    string ModelDirectory,
    string EncoderPath,
    string DecoderPath,
    string TokensPath);

internal static class CreateSubtitlesModelLocator
{
    public static string GetModelDirectory(CreateSubtitlesModelDefinition model) =>
        Path.Combine(AiModelStorage.RootDirectory, model.Folder);

    public static string GetArtifactPath(CreateSubtitlesModelDefinition model, CreateSubtitlesModelArtifact artifact) =>
        Path.Combine(GetModelDirectory(model), artifact.FileName);

    public static CreateSubtitlesModelFiles GetModelFiles(CreateSubtitlesModelDefinition model) => new(
        GetModelDirectory(model),
        GetArtifactPath(model, model.Artifacts[0]),
        GetArtifactPath(model, model.Artifacts[1]),
        GetArtifactPath(model, model.Artifacts[2]));

    public static void EnsureDirectoryExists(CreateSubtitlesModelDefinition model) =>
        AiModelStorage.EnsureDirectory(GetModelDirectory(model));
}
