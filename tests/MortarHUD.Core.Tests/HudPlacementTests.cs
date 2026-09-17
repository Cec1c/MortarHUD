using System.Drawing;
using MortarHUD.Core.Session;
using MortarHUD.Core.Themes;
using Xunit;

namespace MortarHUD.Core.Tests;

/// <summary>
/// HUD 窗口落位（TDD §21.1）。
/// </summary>
/// <remarks>
/// 这里钉住的是「永远找得回来」：用户改过分辨率或显示缩放之后，旧的偏移量
/// 可能把 HUD 整个推到屏幕外——界面上没有恢复入口，只能去改设置文件。
/// </remarks>
public class HudPlacementTests
{
    private static readonly Rectangle WorkArea = new(0, 0, 1920, 1080);
    private const double Width = 200;
    private const double Height = 100;

    [Fact]
    public void Resolve_CenterLeft_AppliesOffsetFromLeftEdge()
    {
        var (x, y) = HudPlacement.Resolve(WorkArea, Width, Height, HudAnchor.CenterLeft, 100, 0, 1);

        Assert.Equal(100, x, 3);
        Assert.Equal((1080 - Height) / 2, y, 3);
    }

    [Fact]
    public void Resolve_TopRight_MeasuresBackFromRightEdge()
    {
        var (x, _) = HudPlacement.Resolve(WorkArea, Width, Height, HudAnchor.TopRight, 50, 0, 1);

        Assert.Equal(1920 - Width - 50, x, 3);
    }

    [Fact]
    public void Resolve_ScalesOffsetWithDpi()
    {
        // 偏移量存的是 DIP，落位要换成物理像素——125% 缩放下 100 对应 125。
        var (x, _) = HudPlacement.Resolve(WorkArea, Width, Height, HudAnchor.CenterLeft, 100, 0, 1.25);

        Assert.Equal(125, x, 3);
    }

    [Fact]
    public void Resolve_OffsetPushesPastRightEdge_PullsBackIntoView()
    {
        // 这是用户踩到的场景：偏移量大到把窗口整个推出屏幕右侧。
        var (x, _) = HudPlacement.Resolve(WorkArea, Width, Height, HudAnchor.CenterLeft, 5000, 0, 1);

        Assert.True(x <= WorkArea.Right - HudPlacement.KeepVisiblePixels,
            $"窗口左缘 {x} 应被拉回到 {WorkArea.Right - HudPlacement.KeepVisiblePixels} 以内");
        Assert.True(x + Width > WorkArea.Right - HudPlacement.KeepVisiblePixels,
            "至少要有 KeepVisiblePixels 宽留在工作区内");
    }

    [Fact]
    public void Resolve_NegativeOffsetPushesPastLeftEdge_PullsBackIntoView()
    {
        var (x, _) = HudPlacement.Resolve(WorkArea, Width, Height, HudAnchor.TopLeft, -5000, 0, 1);

        Assert.True(x >= WorkArea.Left - Width + HudPlacement.KeepVisiblePixels,
            $"窗口左缘 {x} 应被拉回来");
    }

    [Fact]
    public void Resolve_OffsetPushesPastBottomEdge_PullsBackIntoView()
    {
        var (_, y) = HudPlacement.Resolve(WorkArea, Width, Height, HudAnchor.TopLeft, 0, 5000, 1);

        Assert.True(y <= WorkArea.Bottom - HudPlacement.KeepVisiblePixels,
            $"窗口顶缘 {y} 应被拉回到 {WorkArea.Bottom - HudPlacement.KeepVisiblePixels} 以内");
    }

    [Fact]
    public void Resolve_WorkAreaOnSecondaryMonitor_KeepsResultInsideThatMonitor()
    {
        // 副显示器在主屏左边时 Left 是负数，钳制必须按工作区自身算，不能按屏幕原点算。
        var secondary = new Rectangle(-1920, 0, 1920, 1080);

        var (x, _) = HudPlacement.Resolve(secondary, Width, Height, HudAnchor.CenterLeft, 5000, 0, 1);

        Assert.True(x <= secondary.Right - HudPlacement.KeepVisiblePixels);
        Assert.True(x >= secondary.Left - Width + HudPlacement.KeepVisiblePixels);
    }

    [Fact]
    public void Resolve_WorkAreaNarrowerThanKeepVisible_DoesNotThrow()
    {
        // 工作区比 KeepVisiblePixels 还窄时，钳制区间会翻转——
        // 那段代码不能抛 ArgumentException，否则 HUD 直接崩掉。
        var tiny = new Rectangle(0, 0, 10, 1080);
        var (x, _) = HudPlacement.Resolve(tiny, 50, Height, HudAnchor.CenterLeft, 5000, 0, 1);

        Assert.True(double.IsFinite(x));
    }
}
