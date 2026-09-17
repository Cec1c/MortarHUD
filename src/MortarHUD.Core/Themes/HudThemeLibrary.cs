namespace MortarHUD.Core.Themes;

/// <summary>
/// 随程序发布的内置主题（TDD §31 / §48）。
/// 内置主题不可删除，用户只能 Duplicate 出副本再改。
/// </summary>
public static class HudThemeLibrary
{
    /// <summary>内置主题的顺序即设置页里主题卡片的顺序。</summary>
    public static IReadOnlyList<HudTheme> BuiltIn { get; } =
    [
        CreateDefaultGreen(),
        CreateTacticalWhite(),
        CreateAmber(),
        CreateHighContrast(),
    ];

    /// <summary>
    /// MortarHUD 的默认视觉语言（TDD §48）：紧凑、绿色、无背景板、
    /// 细描边 + 软阴影，保证在亮地图和暗地图上都看得清。
    /// </summary>
    public static HudTheme CreateDefaultGreen() => new()
    {
        Name = HudTheme.DefaultGreenName,
        IsBuiltIn = true,
        FontFamily = "Cascadia Mono",
        FontSize = 22,
        FontWeight = "SemiBold",
        Layout = HudLayout.Compact,
        PrimaryColor = "#7CFF6B",
        SecondaryColor = "#B8FFAF",
        SuccessColor = "#7CFF6B",
        WarningColor = "#FFD866",
        ErrorColor = "#FF6464",
        Outline = OutlineWeight.Thin,
        OutlineColor = "#80000000",
        OutlineThickness = 1.0,
        Shadow = ShadowMode.Soft,
        Background = HudBackgroundMode.None,
        Opacity = 1.0,
    };

    /// <summary>中性白，适合绿色和地图撞色的场景。</summary>
    public static HudTheme CreateTacticalWhite() => new()
    {
        Name = HudTheme.TacticalWhiteName,
        IsBuiltIn = true,
        FontFamily = "Cascadia Mono",
        FontSize = 22,
        FontWeight = "SemiBold",
        Layout = HudLayout.Compact,
        PrimaryColor = "#FFFFFF",
        SecondaryColor = "#D6D6D6",
        SuccessColor = "#FFFFFF",
        WarningColor = "#FFD866",
        ErrorColor = "#FF6464",
        Outline = OutlineWeight.Medium,
        OutlineColor = "#B0000000",
        OutlineThickness = 1.4,
        Shadow = ShadowMode.Soft,
        ShadowColor = "#E0000000",
        Background = HudBackgroundMode.None,
        Opacity = 1.0,
    };

    /// <summary>琥珀色，夜间 / 暗地图下刺眼程度更低。</summary>
    public static HudTheme CreateAmber() => new()
    {
        Name = HudTheme.AmberName,
        IsBuiltIn = true,
        FontFamily = "Cascadia Mono",
        FontSize = 22,
        FontWeight = "SemiBold",
        Layout = HudLayout.Compact,
        PrimaryColor = "#FFB000",
        SecondaryColor = "#FFD27A",
        SuccessColor = "#FFB000",
        WarningColor = "#FFE08A",
        ErrorColor = "#FF6B4A",
        Outline = OutlineWeight.Thin,
        OutlineColor = "#80000000",
        OutlineThickness = 1.0,
        Shadow = ShadowMode.Soft,
        Background = HudBackgroundMode.None,
        Opacity = 1.0,
    };

    /// <summary>亮底地图上也要读得清：实心底板 + 厚描边。</summary>
    public static HudTheme CreateHighContrast() => new()
    {
        Name = HudTheme.HighContrastName,
        IsBuiltIn = true,
        FontFamily = "Consolas",
        FontSize = 24,
        FontWeight = "Bold",
        Layout = HudLayout.Compact,
        PrimaryColor = "#FFFFFF",
        SecondaryColor = "#FFFFFF",
        SuccessColor = "#FFFFFF",
        WarningColor = "#FFD866",
        ErrorColor = "#FF9A9A",
        Outline = OutlineWeight.Thick,
        OutlineColor = "#FF000000",
        OutlineThickness = 2.0,
        Shadow = ShadowMode.Hard,
        ShadowColor = "#FF000000",
        Background = HudBackgroundMode.SolidPanel,
        BackgroundColor = "#FF000000",
        BackgroundOpacity = 0.85,
        CornerRadius = 4,
        Padding = 10,
        Opacity = 1.0,
    };

    public static HudTheme? FindBuiltIn(string? name)
        => name is null
            ? null
            : BuiltIn.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
}
