namespace MortarHUD.Core.Themes;

/// <summary>HUD 锚点（TDD §21.1）。配合 Offset 使用，比绝对坐标更能适应不同分辨率。</summary>
public enum HudAnchor
{
    TopLeft,
    TopCenter,
    TopRight,
    CenterLeft,
    Center,
    CenterRight,
    BottomLeft,
    BottomCenter,
    BottomRight,
}

/// <summary>HUD 布局预设（TDD §25）。</summary>
public enum HudLayout
{
    /// <summary>只有数值，没有标签。</summary>
    Minimal,

    /// <summary>AZ / RNG 两行。</summary>
    Compact,

    /// <summary>炮位、目标、AZ、RNG 四行。</summary>
    Detailed,

    /// <summary>AZ 与 RNG 同一行。</summary>
    Horizontal,
}

/// <summary>文字描边粗细（TDD §24）。</summary>
public enum OutlineWeight
{
    Off,
    Thin,
    Medium,
    Thick,
}

/// <summary>阴影模式（TDD §24）。</summary>
public enum ShadowMode
{
    Off,
    Soft,
    Hard,
}

/// <summary>背景模式（TDD §24）。</summary>
public enum HudBackgroundMode
{
    None,
    TransparentPanel,
    SolidPanel,
}

/// <summary>文本对齐。</summary>
public enum HudTextAlignment
{
    Left,
    Center,
    Right,
}
