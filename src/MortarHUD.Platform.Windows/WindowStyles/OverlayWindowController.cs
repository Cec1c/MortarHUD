using System.Runtime.InteropServices;
using MortarHUD.Platform.Windows.NativeMethods;

namespace MortarHUD.Platform.Windows.WindowStyles;

/// <summary>
/// 把 WPF 窗口改造成 Crosshair-X 风格的 Overlay（TDD §19.1）。
/// </summary>
/// <remarks>
/// <para>
/// 需要的扩展样式：
/// <list type="bullet">
///   <item><c>WS_EX_LAYERED</c> —— 允许 WPF 的 AllowsTransparency 生效。</item>
///   <item><c>WS_EX_TRANSPARENT</c> —— 鼠标穿透，游戏照常收到点击。</item>
///   <item><c>WS_EX_TOOLWINDOW</c> —— 不出现在任务栏和 Alt+Tab 列表里。</item>
///   <item><c>WS_EX_NOACTIVATE</c> —— 点它也不抢焦点，游戏不会因此最小化。</item>
/// </list>
/// </para>
/// <para>
/// 「用户按 Alt+Tab 看不到它」是刻意的：HUD 是贴在游戏上的附加信息，
/// 不该混进窗口切换列表里干扰操作。
/// </para>
/// </remarks>
public static class OverlayWindowController
{
    /// <summary>加上 Overlay 需要的全部扩展样式，默认带鼠标穿透。</summary>
    public static void ApplyOverlayStyles(IntPtr hwnd, bool clickThrough)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var style = Win32.GetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE).ToInt64();
        style |= Win32.WS_EX_LAYERED
                 | Win32.WS_EX_TOOLWINDOW
                 | Win32.WS_EX_NOACTIVATE;

        if (clickThrough)
        {
            style |= Win32.WS_EX_TRANSPARENT;
        }
        else
        {
            style &= ~(long)Win32.WS_EX_TRANSPARENT;
        }

        Win32.SetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE, new IntPtr(style));
    }

    /// <summary>
    /// 切换鼠标穿透。
    /// </summary>
    /// <param name="clickThrough">
    /// true = 正常游戏状态，HUD 完全不吃鼠标；
    /// false = 「编辑 HUD」模式，允许拖动（TDD §19.1 / §21.1）。
    /// </param>
    public static void SetClickThrough(IntPtr hwnd, bool clickThrough)
        => ApplyOverlayStyles(hwnd, clickThrough);

    /// <summary>
    /// 显示窗口但不激活它。
    /// </summary>
    /// <remarks>
    /// WPF 的 <c>Show()</c> 会调 SetForegroundWindow，全屏游戏会因此被切出去。
    /// HUD 是贴在游戏上的附加信息，任何时候都不该抢焦点（TDD §19.1）。
    /// </remarks>
    public static void ShowWithoutActivating(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        Win32.SetWindowPos(
            hwnd, IntPtr.Zero, 0, 0, 0, 0,
            Win32.SWP_SHOWWINDOW | Win32.SWP_NOACTIVATE | Win32.SWP_NOMOVE | Win32.SWP_NOSIZE);
    }

    /// <summary>置顶。</summary>
    public static void SetTopmost(IntPtr hwnd, bool topmost)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        Win32.SetWindowPos(
            hwnd,
            topmost ? Win32.HWND_TOPMOST : Win32.HWND_NOTOPMOST,
            0, 0, 0, 0,
            Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
    }

    /// <summary>把窗口挪到物理像素坐标（WPF 的 Left/Top 是 DIP，不能直接用）。</summary>
    public static void MoveToPhysical(IntPtr hwnd, int x, int y)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        Win32.SetWindowPos(
            hwnd, IntPtr.Zero, x, y, 0, 0,
            Win32.SWP_NOSIZE | Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
    }

    public static void PlaceInClientBounds(IntPtr hwnd, System.Drawing.Rectangle bounds)
    {
        Win32.SetWindowPos(hwnd, Win32.HWND_TOPMOST, bounds.X, bounds.Y, bounds.Width, bounds.Height,
            Win32.SWP_NOACTIVATE);
    }

    /// <summary>
    /// 窗口所在显示器的工作区（物理像素，已扣掉任务栏）。
    /// </summary>
    /// <remarks>
    /// 锚点定位必须用物理像素 + 显示器工作区，不能用 <c>SystemParameters.WorkArea</c>：
    /// 后者是 DIP 且只反映主显示器，在多显示器 + 不同缩放的机器上会把 HUD 放歪。
    /// </remarks>
    public static System.Drawing.Rectangle GetMonitorWorkArea(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return System.Drawing.Rectangle.Empty;
        }

        var monitor = Win32.MonitorFromWindow(hwnd, Win32.MONITOR_DEFAULTTONEAREST);
        if (monitor == IntPtr.Zero)
        {
            return System.Drawing.Rectangle.Empty;
        }

        var info = new Win32.MONITORINFO { cbSize = (uint)Marshal.SizeOf<Win32.MONITORINFO>() };
        if (!Win32.GetMonitorInfo(monitor, ref info))
        {
            return System.Drawing.Rectangle.Empty;
        }

        var work = info.rcWork;
        return new System.Drawing.Rectangle(work.Left, work.Top, work.Right - work.Left, work.Bottom - work.Top);
    }

    /// <summary>
    /// 某个物理像素点所在显示器的范围（物理像素）。
    /// </summary>
    /// <remarks>
    /// ROI 的自动缩放必须以<strong>物理像素的屏幕高度</strong>为基准。
    /// 之前这里错用了「虚拟桌面高度 ÷ DPI 缩放系数」，在 125% 缩放的 1080p 上会算出 864，
    /// 于是 AutoScale 变成 0.8、ROI 缩到 120x112，正好把坐标文字切掉——
    /// 表现为时好时坏的识别失败，极难定位。
    /// </remarks>
    public static System.Drawing.Rectangle GetMonitorBoundsForPoint(int physicalX, int physicalY)
    {
        var point = new Win32.POINT { X = physicalX, Y = physicalY };
        var monitor = Win32.MonitorFromPoint(point, Win32.MONITOR_DEFAULTTONEAREST);

        if (monitor == IntPtr.Zero)
        {
            return System.Drawing.Rectangle.Empty;
        }

        var info = new Win32.MONITORINFO { cbSize = (uint)Marshal.SizeOf<Win32.MONITORINFO>() };
        if (!Win32.GetMonitorInfo(monitor, ref info))
        {
            return System.Drawing.Rectangle.Empty;
        }

        var bounds = info.rcMonitor;
        return new System.Drawing.Rectangle(
            bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
    }

    /// <summary>虚拟桌面（所有显示器合起来）的物理像素范围，用于判断 ROI 是否越界。</summary>
    public static System.Drawing.Rectangle GetVirtualScreenBounds()
    {
        var x = Win32.GetSystemMetrics(Win32.SM_XVIRTUALSCREEN);
        var y = Win32.GetSystemMetrics(Win32.SM_YVIRTUALSCREEN);
        var width = Win32.GetSystemMetrics(Win32.SM_CXVIRTUALSCREEN);
        var height = Win32.GetSystemMetrics(Win32.SM_CYVIRTUALSCREEN);

        if (width <= 0 || height <= 0)
        {
            return System.Drawing.Rectangle.Empty;
        }

        return new System.Drawing.Rectangle(x, y, width, height);
    }
}
