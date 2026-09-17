using MortarHUD.Core.Themes;

namespace MortarHUD.Core.Configuration;

/// <summary>HUD 设置页（TDD §21 / §30）。</summary>
public sealed class HudSettings
{
    public bool Visible { get; set; } = true;

    /// <summary>当前生效的视觉效果。实时编辑的就是这一份。</summary>
    public HudTheme CurrentTheme { get; set; } = HudThemeLibrary.CreateDefaultGreen();

    /// <summary>当前视觉效果来自哪个已保存主题（用户改动后可能与主题内容不再一致）。</summary>
    public string ActiveThemeName { get; set; } = HudTheme.DefaultGreenName;

    // ---- 位置（TDD §21.1）----

    public HudAnchor Anchor { get; set; } = HudAnchor.CenterLeft;

    public double OffsetX { get; set; } = 40;

    public double OffsetY { get; set; }

    /// <summary>false = 鼠标穿透（正常游戏状态）；true = 可拖动编辑。</summary>
    public bool PositionUnlocked { get; set; }

    // ---- 显示字段 ----

    public bool ShowAz { get; set; } = true;

    public bool ShowRng { get; set; } = true;

    public bool ShowGun { get; set; }

    public bool ShowTarget { get; set; }

    /// <summary>方位角小数位。TDD §20 默认显示 079.0°，即 1 位。</summary>
    public int BearingDecimals { get; set; } = 1;

    /// <summary>距离小数位。TDD §20 默认显示 152m，即 0 位。</summary>
    public int RangeDecimals { get; set; }

    public HudSettings Clone() => new()
    {
        Visible = Visible,
        CurrentTheme = CurrentTheme.Clone(),
        ActiveThemeName = ActiveThemeName,
        Anchor = Anchor,
        OffsetX = OffsetX,
        OffsetY = OffsetY,
        PositionUnlocked = PositionUnlocked,
        ShowAz = ShowAz,
        ShowRng = ShowRng,
        ShowGun = ShowGun,
        ShowTarget = ShowTarget,
        BearingDecimals = BearingDecimals,
        RangeDecimals = RangeDecimals,
    };
}
