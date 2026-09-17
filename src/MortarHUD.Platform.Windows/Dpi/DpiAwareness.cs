using MortarHUD.Platform.Windows.NativeMethods;

namespace MortarHUD.Platform.Windows.Dpi;

/// <summary>
/// DPI 相关处理（TDD §12）。
/// </summary>
/// <remarks>
/// <para>
/// 全项目的坐标约定：<strong>底层一律用物理像素</strong>。
/// <c>GetCursorPos</c>、ROI 截图、Overlay 定位都在物理像素空间里工作，
/// 只有 WPF 展示层在最后一步做 DIP 转换。
/// </para>
/// <para>
/// 进程必须声明 Per-Monitor V2，否则 <c>GetCursorPos</c> 返回的是被系统
/// 虚拟化过的坐标，和截图拿到的像素对不上——那种偏移很难查。
/// </para>
/// </remarks>
public static class DpiAwareness
{
    private static bool _initialized;

    /// <summary>
    /// 声明 Per-Monitor V2 DPI 感知。必须在创建任何窗口之前调用一次。
    /// </summary>
    /// <returns>true 表示设置成功；false 表示已被其它方式设置（通常也无害）。</returns>
    public static bool EnablePerMonitorV2()
    {
        if (_initialized)
        {
            return true;
        }

        var ok = Win32.SetProcessDpiAwarenessContext(
            new IntPtr(Win32.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2));

        _initialized = true;
        return ok;
    }

    /// <summary>某个窗口当前所在显示器的缩放系数（1.0 = 96 DPI）。</summary>
    public static double GetScaleForWindow(IntPtr hwnd)
    {
        var dpi = hwnd != IntPtr.Zero ? Win32.GetDpiForWindow(hwnd) : 0;
        if (dpi == 0)
        {
            dpi = (uint)Win32.GetDpiForSystem();
        }

        return dpi <= 0 ? 1.0 : dpi / 96.0;
    }

    /// <summary>某个物理像素点所在显示器的缩放系数。</summary>
    public static double GetScaleForPoint(int physicalX, int physicalY)
    {
        var point = new Win32.POINT { X = physicalX, Y = physicalY };
        var monitor = Win32.MonitorFromPoint(point, Win32.MONITOR_DEFAULTTONEAREST);

        if (monitor != IntPtr.Zero
            && Win32.GetDpiForMonitor(monitor, Win32.MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0
            && dpiX > 0)
        {
            return dpiX / 96.0;
        }

        var fallback = Win32.GetDpiForSystem();
        return fallback <= 0 ? 1.0 : fallback / 96.0;
    }
}
