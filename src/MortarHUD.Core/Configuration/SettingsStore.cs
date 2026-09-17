using System.Text.Json;
using System.Text.Json.Serialization;
using MortarHUD.Core.Themes;

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
            return new MortarHudSettings();
        }

        try
        {
            var json = File.ReadAllText(_settingsPath);
            var settings = JsonSerializer.Deserialize<MortarHudSettings>(json, SerializerOptions);
            if (settings is null)
            {
                return new MortarHudSettings();
            }

            return Migrate(settings);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            TryQuarantineCorruptFile(ex);
            return new MortarHudSettings();
        }
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
