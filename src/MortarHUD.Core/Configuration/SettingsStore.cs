using System.Text.Json;
using System.Text.Json.Serialization;
using MortarHUD.Core.Themes;
using MortarHUD.Localization;

namespace MortarHUD.Core.Configuration;

/// <summary>
/// 设置的加载 / 保存。写入采用「先写临时文件再替换」，避免断电或崩溃留下半个 JSON。
/// </summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _settingsPath;

    public SettingsStore(string? settingsPath = null)
        => _settingsPath = settingsPath ?? AppPaths.SettingsFile;

    public string SettingsPath => _settingsPath;

    /// <summary>加载设置；文件不存在或损坏时回退到默认值，并把坏文件改名留证。</summary>
    public MortarHudSettings Load()
    {
        if (!File.Exists(_settingsPath))
        {
            return CreateFirstRunDefaults();
        }

        try
        {
            var json = File.ReadAllText(_settingsPath);
            var settings = JsonSerializer.Deserialize<MortarHudSettings>(json, SerializerOptions);
            if (settings is null)
            {
                return CreateFirstRunDefaults();
            }

            return Migrate(settings);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            TryQuarantineCorruptFile(ex);
            return CreateFirstRunDefaults();
        }
    }

    /// <summary>
    /// 没有可用配置时的起点：按系统语言挑界面语言。
    /// </summary>
    /// <remarks>
    /// 坏文件被隔离之后也走这里——那种情况实质上等于重新装一次，
    /// 沿用「首次启动」的规则比硬塞中文更合理。
    /// </remarks>
    private static MortarHudSettings CreateFirstRunDefaults()
    {
        var settings = new MortarHudSettings();
        settings.General.Language = SystemLanguage.Detect();
        return settings;
    }

    public void Save(MortarHudSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var directory = Path.GetDirectoryName(_settingsPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        settings.SchemaVersion = MortarHudSettings.CurrentSchemaVersion;

        var json = JsonSerializer.Serialize(settings, SerializerOptions);
        var tempPath = _settingsPath + ".tmp";
        File.WriteAllText(tempPath, json);

        // File.Move(overwrite) 在 NTFS 上是原子的，比先删后写安全。
        File.Move(tempPath, _settingsPath, overwrite: true);
    }

    /// <summary>按 schemaVersion 做向前兼容。</summary>
    private static MortarHudSettings Migrate(MortarHudSettings settings)
    {
        if (settings.SchemaVersion <= 0)
        {
            settings.SchemaVersion = 1;
        }

        // v1 → v2：记录目标的默认键从 F7 改成鼠标中键。
        //
        // 只在用户没动过这个绑定（值仍是老默认 F7）时才改——
        // 如果他早就自定义成别的键，那是他的选择，迁移不该覆盖。
        if (settings.SchemaVersion < 2)
        {
            if (string.Equals(settings.Hotkeys.CaptureTarget, "F7", StringComparison.OrdinalIgnoreCase))
            {
                settings.Hotkeys.CaptureTarget = new HotkeySettings().CaptureTarget;
            }

            settings.SchemaVersion = 2;
        }

        // v2 → v3：ROI 默认窗口收紧。
        //
        // 原来的 150x140 是给旧锚点 (+21,-57) 留的 ±30px 容错，而那个锚点偏了约 15px。
        // 窗口过大把地图网格线也框了进来——网格线是半透明白，比坐标文字还亮，
        // 二值化后变成粗黑块，与 y、x 两个轴字母粘连，直接啃掉轴字母，
        // 识别结果退化成 "10.29" 这种残片，解析器随即判 X_NOT_FOUND。
        // 用 35 份「整屏 + 光标位置」同刻采样重新标定后，收紧到文字块外围 +12px。
        //
        // 同样只在用户没动过 ROI 时才迁移（四个值都还是旧默认）。
        if (settings.SchemaVersion < 3)
        {
            if (settings.Roi is { Width: 150, Height: 140, OffsetX: -15, OffsetY: -90 })
            {
                settings.Roi.Width = RoiSettings.ReferenceWidth;
                settings.Roi.Height = RoiSettings.ReferenceHeight;
                settings.Roi.OffsetX = RoiSettings.ReferenceOffsetX;
                settings.Roi.OffsetY = RoiSettings.ReferenceOffsetY;
            }

            settings.SchemaVersion = 3;
        }

        // v3 → v4：PageSegMode 默认从 6（单一文本块）换成 11（稀疏文本）。
        //
        // 坐标是「y 行在上、x 行在下」的两行稀疏文字，6 会把两行当成一个文本块
        // 统一切分，实测会把末位数字读错（同一张图 A 读 99.75、C 读 99.73），
        // 触发 PIPELINE_DISAGREEMENT 判失败。117 份实机采集回放：
        // 6 → 成功 65 / 两票矛盾 16；11 → 成功 79 / 两票矛盾 5。
        //
        // 同样只在用户没动过这个值（还是旧默认 6）时才迁移。
        if (settings.SchemaVersion < 4)
        {
            if (settings.Ocr is { PageSegMode: 6 })
            {
                settings.Ocr.PageSegMode = new OcrSettings().PageSegMode;
            }

            settings.SchemaVersion = 4;
        }

        // 兜底：老文件里缺少的嵌套对象补上，避免 NRE。
        settings.General ??= new GeneralSettings();
        settings.Hotkeys ??= new HotkeySettings();
        settings.Hud ??= new HudSettings();
        settings.Roi ??= new RoiSettings();
        settings.Ocr ??= new OcrSettings();
        settings.Debug ??= new DebugSettings();
        settings.Hud.CurrentTheme ??= HudThemeLibrary.CreateDefaultGreen();

        return settings;
    }

    private void TryQuarantineCorruptFile(Exception cause)
    {
        try
        {
            var quarantine = _settingsPath + $".corrupt-{DateTime.Now:yyyyMMddHHmmss}";
            File.Move(_settingsPath, quarantine, overwrite: true);
            System.Diagnostics.Debug.WriteLine(
                $"settings.json 解析失败，已备份为 {quarantine}：{cause.Message}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 连备份都失败就只能放弃了，至少不要让程序起不来。
        }
    }
}
