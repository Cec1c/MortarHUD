using MortarHUD.Core.Configuration;
using MortarHUD.Localization;
using MortarHUD.Core.Themes;
using Xunit;

namespace MortarHUD.Core.Tests;

/// <summary>设置与主题持久化测试（TDD §26 / §31 / §35）。</summary>
public sealed class SettingsAndThemeTests : IDisposable
{
    private readonly string _sandbox;

    public SettingsAndThemeTests()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), "MortarHUD.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sandbox);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_sandbox, recursive: true);
        }
        catch (IOException)
        {
            // 清理失败不影响测试结论。
        }
    }

    // ------------------------------------------------------------ 设置

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var store = new SettingsStore(Path.Combine(_sandbox, "settings.json"));

        var settings = store.Load();

        Assert.Equal(MortarHudSettings.CurrentSchemaVersion, settings.SchemaVersion);
        Assert.Equal(100.0, settings.General.MetersPerCoordinateUnit);
        Assert.Equal("F6", settings.Hotkeys.CaptureGun);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsAllGroups()
    {
        var path = Path.Combine(_sandbox, "settings.json");
        var store = new SettingsStore(path);

        var original = new MortarHudSettings();
        original.General.MetersPerCoordinateUnit = 250;
        original.General.XPositiveDirection = "West";
        original.Hotkeys.CaptureGun = "Ctrl+Shift+G";
        original.Hud.CurrentTheme.PrimaryColor = "#FF00FF";
        original.Hud.CurrentTheme.FontSize = 33;
        original.Hud.Anchor = HudAnchor.BottomRight;
        original.Roi.OffsetX = -42;
        original.Ocr.MinimumConfidence = 0.42;
        original.Debug.Enabled = true;

        store.Save(original);
        var loaded = store.Load();

        Assert.Equal(250, loaded.General.MetersPerCoordinateUnit);
        Assert.Equal("West", loaded.General.XPositiveDirection);
        Assert.Equal("Ctrl+Shift+G", loaded.Hotkeys.CaptureGun);
        Assert.Equal("#FF00FF", loaded.Hud.CurrentTheme.PrimaryColor);
        Assert.Equal(33, loaded.Hud.CurrentTheme.FontSize);
        Assert.Equal(HudAnchor.BottomRight, loaded.Hud.Anchor);
        Assert.Equal(-42, loaded.Roi.OffsetX);
        Assert.Equal(0.42, loaded.Ocr.MinimumConfidence, precision: 9);
        Assert.True(loaded.Debug.Enabled);
    }

    /// <summary>
    /// 没有配置文件时（首次启动），界面语言按系统语言挑。
    /// </summary>
    /// <remarks>
    /// 断言两边都用同一台机器上的检测结果，所以这条测试在中文和英文的 CI 上都能过。
    /// 检测规则本身由 <c>LocTableTests</c> 用注入的语言逐条钉住。
    /// </remarks>
    [Fact]
    public void Load_MissingFile_PicksLanguageFromSystem()
    {
        var store = new SettingsStore(Path.Combine(_sandbox, "settings.json"));

        Assert.Equal(SystemLanguage.Detect(), store.Load().General.Language);
    }

    /// <summary>用户挑过的语言不能被「首次启动」的逻辑覆盖。</summary>
    [Fact]
    public void Load_KeepsChosenLanguage()
    {
        var path = Path.Combine(_sandbox, "settings.json");
        File.WriteAllText(path, """
            { "schemaVersion": 4, "general": { "language": "en" } }
            """);

        Assert.Equal("en", new SettingsStore(path).Load().General.Language);
    }

    /// <summary>
    /// v3 → v4：PageSegMode 的旧默认 6 要迁到 11。
    /// </summary>
    /// <remarks>
    /// 6（单一文本块）会把坐标的上下两行当成一个块统一切分，实测会把末位数字读错，
    /// 触发 PIPELINE_DISAGREEMENT。这里的重点不是新值是多少，而是
    /// <strong>只有用户没动过这个值时才迁移</strong>。
    /// </remarks>
    [Fact]
    public void Load_MigratesDefaultPageSegModeFrom6To11()
    {
        var path = Path.Combine(_sandbox, "settings.json");
        File.WriteAllText(path, """
            { "schemaVersion": 3, "ocr": { "pageSegMode": 6 } }
            """);

        var settings = new SettingsStore(path).Load();

        Assert.Equal(new OcrSettings().PageSegMode, settings.Ocr.PageSegMode);
        Assert.Equal(MortarHudSettings.CurrentSchemaVersion, settings.SchemaVersion);
    }

    /// <summary>用户自己挑过 PageSegMode 就不能被迁移覆盖。</summary>
    [Fact]
    public void Load_KeepsCustomizedPageSegMode()
    {
        var path = Path.Combine(_sandbox, "settings.json");
        File.WriteAllText(path, """
            { "schemaVersion": 3, "ocr": { "pageSegMode": 3 } }
            """);

        var settings = new SettingsStore(path).Load();

        Assert.Equal(3, settings.Ocr.PageSegMode);
    }

    /// <summary>枚举以字符串写入，方便用户手改 JSON，也避免以后调整枚举顺序时读串。</summary>
    [Fact]
    public void Save_WritesEnumsAsStrings()
    {
        var path = Path.Combine(_sandbox, "settings.json");
        var store = new SettingsStore(path);

        var settings = new MortarHudSettings();
        settings.Hud.CurrentTheme.Layout = HudLayout.Horizontal;
        store.Save(settings);

        var json = File.ReadAllText(path);

        Assert.Contains("\"Horizontal\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"layout\": 3", json, StringComparison.Ordinal);
    }

    /// <summary>损坏的 settings.json 不该让程序起不来。</summary>
    [Fact]
    public void Load_CorruptFile_FallsBackToDefaultsAndQuarantines()
    {
        var path = Path.Combine(_sandbox, "settings.json");
        File.WriteAllText(path, "{ this is not json");

        var store = new SettingsStore(path);
        var settings = store.Load();

        Assert.Equal(MortarHudSettings.CurrentSchemaVersion, settings.SchemaVersion);
        Assert.False(File.Exists(path));
        Assert.NotEmpty(Directory.GetFiles(_sandbox, "settings.json.corrupt-*"));
    }

    [Fact]
    public void Save_IsAtomic_NoTemporaryFileLeftBehind()
    {
        var path = Path.Combine(_sandbox, "settings.json");
        var store = new SettingsStore(path);

        store.Save(new MortarHudSettings());

        Assert.True(File.Exists(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    // ------------------------------------------------------------ 主题

    [Fact]
    public void BuiltInLibrary_HasFourThemesPerTdd()
    {
        var names = HudThemeLibrary.BuiltIn.Select(t => t.Name).ToList();

        Assert.Contains(HudTheme.DefaultGreenName, names);
        Assert.Contains(HudTheme.TacticalWhiteName, names);
        Assert.Contains(HudTheme.AmberName, names);
        Assert.Contains(HudTheme.HighContrastName, names);
        Assert.All(HudThemeLibrary.BuiltIn, t => Assert.True(t.IsBuiltIn));
    }

    /// <summary>TDD §48：默认主题就该长这样。</summary>
    [Fact]
    public void DefaultGreen_MatchesTddSection48()
    {
        var theme = HudThemeLibrary.CreateDefaultGreen();

        Assert.Equal(HudLayout.Compact, theme.Layout);
        Assert.Equal("Cascadia Mono", theme.FontFamily);
        Assert.Equal(22, theme.FontSize);
        Assert.Equal("SemiBold", theme.FontWeight);
        Assert.Equal("#7CFF6B", theme.PrimaryColor);
        Assert.Equal(HudBackgroundMode.None, theme.Background);
        Assert.Equal(OutlineWeight.Thin, theme.Outline);
        Assert.Equal(ShadowMode.Soft, theme.Shadow);
        Assert.Equal(1.0, theme.Opacity, precision: 9);
    }

    [Fact]
    public void ThemeStore_SaveThenLoadCustom()
    {
        var directory = Path.Combine(_sandbox, "Themes");
        var store = new HudThemeStore(directory);

        var custom = HudThemeLibrary.CreateDefaultGreen().CloneAsCustom("我的主题");
        custom.PrimaryColor = "#00E5FF";
        store.Save(custom);

        var loaded = store.LoadCustom().Single();

        Assert.Equal("我的主题", loaded.Name);
        Assert.Equal("#00E5FF", loaded.PrimaryColor);
        Assert.False(loaded.IsBuiltIn);
    }

    [Fact]
    public void ThemeStore_DeleteBuiltIn_Throws()
    {
        var store = new HudThemeStore(Path.Combine(_sandbox, "Themes"));

        Assert.Throws<InvalidOperationException>(() => store.Delete(HudTheme.DefaultGreenName));
    }

    [Fact]
    public void ThemeStore_LoadAll_IncludesBuiltInsAndCustom()
    {
        var directory = Path.Combine(_sandbox, "Themes");
        var store = new HudThemeStore(directory);
        store.Save(HudThemeLibrary.CreateDefaultGreen().CloneAsCustom("Custom A"));

        var all = store.LoadAll();

        Assert.Contains(all, t => t.Name == HudTheme.DefaultGreenName && t.IsBuiltIn);
        Assert.Contains(all, t => t.Name == "Custom A" && !t.IsBuiltIn);
    }

    /// <summary>单个坏主题文件不该让整个主题列表加载失败。</summary>
    [Fact]
    public void ThemeStore_SkipsCorruptFiles()
    {
        var directory = Path.Combine(_sandbox, "Themes");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "broken.json"), "{ not json");

        var store = new HudThemeStore(directory);
        var custom = store.LoadCustom();

        Assert.Empty(custom);
        Assert.NotEmpty(store.LoadAll());
    }

    /// <summary>主题名里的非法文件名字符必须被替换，否则保存会抛异常。</summary>
    [Fact]
    public void ThemeStore_SanitizesFileName()
    {
        var store = new HudThemeStore(Path.Combine(_sandbox, "Themes"));

        var path = store.GetPathFor("a/b:c*d?e");

        Assert.DoesNotContain('/', Path.GetFileName(path));
        Assert.DoesNotContain(':', Path.GetFileName(path));
        Assert.DoesNotContain('*', Path.GetFileName(path));
    }

    [Fact]
    public void CloneAsCustom_ClearsBuiltInFlag()
    {
        var copy = HudThemeLibrary.CreateDefaultGreen().CloneAsCustom("副本");

        Assert.Equal("副本", copy.Name);
        Assert.False(copy.IsBuiltIn);
    }

    /// <summary>AppPaths 重定向只影响测试，不碰真实 AppData。</summary>
    [Fact]
    public void AppPaths_OverrideRedirectsEverything()
    {
        var original = AppPaths.Root;

        try
        {
            AppPaths.OverrideRoot(_sandbox);
            AppPaths.EnsureDirectories();

            Assert.StartsWith(_sandbox, AppPaths.SettingsFile, StringComparison.Ordinal);
            Assert.True(Directory.Exists(AppPaths.ThemesDirectory));
            Assert.True(Directory.Exists(AppPaths.LogsDirectory));
            Assert.True(Directory.Exists(AppPaths.DebugDirectory));
        }
        finally
        {
            AppPaths.OverrideRoot(original);
        }
    }
}
