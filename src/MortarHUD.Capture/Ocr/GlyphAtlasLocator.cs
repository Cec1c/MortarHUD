namespace MortarHUD.Capture.Ocr;

/// <summary>
/// 找字形模板库目录（与 <see cref="TessdataLocator"/> 同一套查找策略）。
/// </summary>
public static class GlyphAtlasLocator
{
    public const string FolderName = "glyphs";

    public static string Resolve(string? configuredPath = null)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath) && IsUsable(configuredPath))
        {
            return configuredPath;
        }

        var baseDirectory = AppContext.BaseDirectory;

        foreach (var candidate in EnumerateCandidates(baseDirectory))
        {
            if (IsUsable(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(baseDirectory, "Models", FolderName);
    }

    private static IEnumerable<string> EnumerateCandidates(string baseDirectory)
    {
        yield return Path.Combine(baseDirectory, "Models", FolderName);
        yield return Path.Combine(baseDirectory, FolderName);

        var directory = new DirectoryInfo(baseDirectory);

        // 开发时从 bin 目录往上找项目目录。
        for (var depth = 0; depth < 6 && directory is not null; depth++)
        {
            yield return Path.Combine(directory.FullName, "Models", FolderName);
            directory = directory.Parent;
        }
    }

    public static bool IsUsable(string? directory)
        => !string.IsNullOrWhiteSpace(directory)
           && Directory.Exists(directory)
           && File.Exists(Path.Combine(directory, GlyphAtlas.SpriteFileName))
           && File.Exists(Path.Combine(directory, GlyphAtlas.LabelsFileName));
}
