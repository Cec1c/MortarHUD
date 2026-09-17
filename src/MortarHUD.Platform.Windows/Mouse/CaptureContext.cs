using System.Drawing;
using System.Runtime.InteropServices;

namespace MortarHUD.Platform.Windows.Mouse;

/// <summary>只读前台窗口和光标，避免关图、切窗或移动后继续采用迟到的坐标。</summary>
public static class CaptureContext
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr window, ref Point point);
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }

    public static IntPtr Foreground => GetForegroundWindow();

    public static bool IsExternal(IntPtr window)
    {
        GetWindowThreadProcessId(window, out var process);
        return window != IntPtr.Zero && process != (uint)Environment.ProcessId;
    }

    public static bool TryGetCursor(out Point point)
    {
        var ok = new WindowsCursorPositionProvider().TryGetCursorPosition(out var x, out var y);
        point = new Point(x, y);
        return ok;
    }

    public static bool IsClientCenter(IntPtr window, Point point)
    {
        if (!GetClientRect(window, out var rect)) return false;
        var center = new Point((rect.Right - rect.Left) / 2, (rect.Bottom - rect.Top) / 2);
        return ClientToScreen(window, ref center)
            && Math.Abs(center.X - point.X) <= 8 && Math.Abs(center.Y - point.Y) <= 8;
    }
}
