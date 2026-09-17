using System.Drawing;
using MortarHUD.Core.Themes;

namespace MortarHUD.Core.Session;

/// <summary>
/// HUD 窗口在屏幕上的落位（TDD §21.1）。
/// </summary>
/// <remarks>
/// <para>
/// 锚点决定贴哪条边，偏移量再把它挪开。偏移量由用户在「解锁位置，拖动 HUD」模式下
/// 拖出来并存进设置，落位时乘上 DPI 缩放换算成物理像素。
/// </para>
/// <para>
/// 最后那道钳制是必须的，不是保险起见：用户改过分辨率或显示缩放之后，旧的偏移量
/// 可能把 HUD 整个推到屏幕外——界面上**没有**「把 HUD 找回来」的入口，那就只能
/// 去手改设置文件了。
/// </para>
/// </remarks>
public static class HudPlacement
{
    /// <summary>窗口至少要有这么多像素留在工作区内，否则用户再也找不到它。</summary>
    public const int KeepVisiblePixels = 40;

    public static (double X, double Y) Resolve(
        Rectangle workArea,
        double width,
        double height,
        HudAnchor anchor,
        double offsetX,
        double offsetY,
        double scale)
    {
        var dx = offsetX * scale;
        var dy = offsetY * scale;

        var x = anchor switch
        {
            HudAnchor.TopLeft or HudAnchor.CenterLeft or HudAnchor.BottomLeft
                => workArea.Left + dx,
            HudAnchor.TopCenter or HudAnchor.Center or HudAnchor.BottomCenter
                => workArea.Left + (workArea.Width - width) / 2 + dx,
            _ => workArea.Right - width - dx,
        };

        var y = anchor switch
        {
            HudAnchor.TopLeft or HudAnchor.TopCenter or HudAnchor.TopRight
                => workArea.Top + dy,
            HudAnchor.CenterLeft or HudAnchor.Center or HudAnchor.CenterRight
                => workArea.Top + (workArea.Height - height) / 2 + dy,
            _ => workArea.Bottom - height - dy,
        };

        return (ClampToVisible(x, workArea.Left, workArea.Right, width),
            ClampToVisible(y, workArea.Top, workArea.Bottom, height));
    }

    /// <summary>
    /// 允许窗口探出工作区边缘，但必须留下 <see cref="KeepVisiblePixels"/> 宽（高）在里面。
    /// </summary>
    private static double ClampToVisible(double value, double min, double max, double size)
    {
        var lower = min - size + KeepVisiblePixels;
        var upper = max - KeepVisiblePixels;

        // 工作区比 KeepVisiblePixels 还窄时区间会翻转，贴着起始边放，
        // 至少保证左/上缘可见；这里绝不能抛异常。
        return lower <= upper ? Math.Clamp(value, lower, upper) : lower;
    }
}
