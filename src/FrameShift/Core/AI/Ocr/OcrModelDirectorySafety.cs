namespace FrameShift.Core.AI.Ocr;

internal static class OcrModelDirectorySafety
{
    public static void ValidateDownloadPath(string root, string path)
    {
        if (!AiModelDirectorySafety.IsSameOrChildPath(path, root))
            throw new InvalidOperationException("OCR downloads must stay inside the configured models directory.");
        for (var current = Path.GetFullPath(path); AiModelDirectorySafety.IsSameOrChildPath(current, root); current = Path.GetDirectoryName(current)!)
        {
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("The OCR model path contains a filesystem link. Choose a regular models directory in Settings.");
        }
    }

    public static bool ContainsOnlyExpectedFiles(string root, string directory, OcrModelDefinition model)
    {
        try
        {
            ValidateDownloadPath(root, directory);
            var allowed = new HashSet<string>(model.Files.Select(file => file.Path.Replace('/', Path.DirectorySeparatorChar)), StringComparer.OrdinalIgnoreCase);
            allowed.UnionWith(model.Files.Select(file => file.Path.Replace('/', Path.DirectorySeparatorChar) + ".tmp"));
            allowed.Add(AiModelStorage.ModelDirectoryMarkerFileName);
            allowed.Add(".download-lock");
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                if (!AiModelDirectorySafety.IsSameOrChildPath(entry, root) || (File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0)
                    return false;
                var name = Path.GetFileName(entry);
                if (Directory.Exists(entry))
                {
                    if (name is not "det" and not "rec")
                        return false;
                    foreach (var file in Directory.EnumerateFileSystemEntries(entry))
                    {
                        if ((File.GetAttributes(file) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0 || !allowed.Contains(Path.GetRelativePath(directory, file)))
                            return false;
                    }
                }
                else if (!allowed.Contains(name))
                    return false;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }
}
