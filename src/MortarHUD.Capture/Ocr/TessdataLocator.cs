namespace MortarHUD.Capture.Ocr;

/// <summary>
/// 找 tessdata 目录（TDD §45：语言包随程序发布，用户不装 Tesseract）。
/// </summary>
public static class TessdataLocator
{
    public const string FolderName = "tessdata";
    public const string RequiredFile = "eng.traineddata";

    /// <summary>
    /// 依次尝试：设置里的显式路径 → 程序目录/Models/tessdata → 程序目录/tessdata
    /// → 各层父目录下的 Models/tessdata（开发时从 bin 目录往上找）。
    /// </summary>
    public static string Resolve(string? configuredPath)
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

        // 一个都没找到：把最标准的位置返回去，让 Tesseract 报出可读的错误。
        return Path.Combine(baseDirectory, "Models", FolderName);
    }

    private static IEnumerable<string> EnumerateCandidates(string baseDirectory)
    {
        yield return Path.Combine(baseDirectory, "Models", FolderName);
        yield return Path.Combine(baseDirectory, FolderName);

        // 开发时 BaseDirectory 是 src/MortarHUD.App/bin/Debug/net10.0-windows/，
        // 语言包放在 src/MortarHUD.App/Models/，需要往上找到项目目录。
        var directory = new DirectoryInfo(baseDirectory);

        for (var depth = 0; depth < 6 && directory is not null; depth++)
        {
            yield return Path.Combine(directory.FullName, "Models", FolderName);
            directory = directory.Parent;
        }
    }

    public static bool IsUsable(string? directory)
        => !string.IsNullOrWhiteSpace(directory)
           && Directory.Exists(directory)
           && File.Exists(Path.Combine(directory, RequiredFile));
}
