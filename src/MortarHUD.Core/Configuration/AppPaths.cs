namespace MortarHUD.Core.Configuration;

/// <summary>
/// 所有落盘位置的唯一来源。测试可以通过 <see cref="OverrideRoot"/> 指到临时目录。
/// </summary>
public static class AppPaths
{
    private static string? _overrideRoot;

    public const string AppFolderName = "MortarHUD";

    public static string Root => _overrideRoot ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        AppFolderName);

    public static string SettingsFile => Path.Combine(Root, "settings.json");

    public static string ThemesDirectory => Path.Combine(Root, "Themes");

    public static string LogsDirectory => Path.Combine(Root, "Logs");

    public static string DebugDirectory => Path.Combine(Root, "Debug");

    /// <summary>重定向根目录（仅测试使用）。传 null 恢复默认。</summary>
    public static void OverrideRoot(string? root) => _overrideRoot = root;

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(ThemesDirectory);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(DebugDirectory);
    }
}
