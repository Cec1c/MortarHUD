namespace MortarHUD.Core.Themes;

/// <summary>
/// 一套 HUD 视觉定义，对应 TDD §26 的 JSON 文件。
/// 主题<strong>不</strong>写死在 XAML 里——运行时全部从这里驱动。
/// </summary>
public sealed class HudTheme
{
    /// <summary>内置主题的标识名，用户不能删除或改名。</summary>
    public const string DefaultGreenName = "Default Green";
    public const string TacticalWhiteName = "Tactical White";
    public const string AmberName = "Amber";
    public const string HighContrastName = "High Contrast";

    public string Name { get; set; } = DefaultGreenName;

    /// <summary>true 表示随程序发布的内置主题，不可删除。</summary>
    public bool IsBuiltIn { get; set; }

    // ---- 字体 ----

    public string FontFamily { get; set; } = "Cascadia Mono";

    /// <summary>字号（DIP）。</summary>
    public double FontSize { get; set; } = 22;

    /// <summary>WPF FontWeight 名称：Light / Normal / Medium / SemiBold / Bold。</summary>
    public string FontWeight { get; set; } = "SemiBold";

    public bool Italic { get; set; }

    /// <summary>字间距（像素，正数拉宽）。</summary>
    public double LetterSpacing { get; set; }

    /// <summary>行距倍数。</summary>
    public double LineHeight { get; set; } = 1.15;

    public HudTextAlignment Alignment { get; set; } = HudTextAlignment.Left;

    // ---- 颜色（#RRGGBB 或 #AARRGGBB）----

    public string PrimaryColor { get; set; } = "#7CFF6B";

    public string SecondaryColor { get; set; } = "#B8FFAF";

    public string SuccessColor { get; set; } = "#7CFF6B";

    public string WarningColor { get; set; } = "#FFD866";

    public string ErrorColor { get; set; } = "#FF6464";

    // ---- 描边 ----

    public OutlineWeight Outline { get; set; } = OutlineWeight.Thin;

    public string OutlineColor { get; set; } = "#80000000";

    public double OutlineThickness { get; set; } = 1.0;

    // ---- 阴影 ----

    public ShadowMode Shadow { get; set; } = ShadowMode.Soft;

    public string ShadowColor { get; set; } = "#C0000000";

    // ---- 背景 ----

    public HudBackgroundMode Background { get; set; } = HudBackgroundMode.None;

    public string BackgroundColor { get; set; } = "#80000000";

    /// <summary>背景不透明度 0..1，叠加在 <see cref="BackgroundColor"/> 的 alpha 之上。</summary>
    public double BackgroundOpacity { get; set; } = 0.5;

    public double CornerRadius { get; set; } = 6;

    public double Padding { get; set; } = 8;

    /// <summary>整体不透明度 0..1。</summary>
    public double Opacity { get; set; } = 1.0;

    // ---- 布局 ----

    public HudLayout Layout { get; set; } = HudLayout.Compact;

    public HudTheme Clone() => (HudTheme)MemberwiseClone();

    /// <summary>去掉内置标记后的副本，用于「另存为自定义主题」。</summary>
    public HudTheme CloneAsCustom(string name)
    {
        var copy = Clone();
        copy.Name = name;
        copy.IsBuiltIn = false;
        return copy;
    }
}
