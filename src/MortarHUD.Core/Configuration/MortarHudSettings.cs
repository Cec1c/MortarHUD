namespace MortarHUD.Core.Configuration;

/// <summary>
/// <c>%AppData%/MortarHUD/settings.json</c> 的完整模型（TDD §35）。
/// </summary>
public sealed class MortarHudSettings
{
    /// <summary>当前 schema 版本。以后加字段时用它做 migration。</summary>
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public GeneralSettings General { get; set; } = new();

    public HotkeySettings Hotkeys { get; set; } = new();

    public HudSettings Hud { get; set; } = new();

    public RoiSettings Roi { get; set; } = new();

    public OcrSettings Ocr { get; set; } = new();

    public DebugSettings Debug { get; set; } = new();

    public MortarHudSettings Clone() => new()
    {
        SchemaVersion = SchemaVersion,
        General = General.Clone(),
        Hotkeys = Hotkeys.Clone(),
        Hud = Hud.Clone(),
        Roi = Roi.Clone(),
        Ocr = Ocr.Clone(),
        Debug = Debug.Clone(),
    };
}
