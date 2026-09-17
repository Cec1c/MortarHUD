using System.Runtime.InteropServices;

namespace MortarHUD.Platform.Windows.NativeMethods;

/// <summary>
/// 屏幕捕获用到的 GDI 互操作。
/// </summary>
/// <remarks>
/// 用 BitBlt + DIB Section 直接拿到一块 BGRA 缓冲区，
/// 不经过 GDI+ 的 Bitmap/Graphics，少一次整块拷贝。
/// </remarks>
internal static partial class Gdi32
{
    internal const int SRCCOPY = 0x00CC0020;

    /// <summary>
    /// 让 BitBlt 把分层窗口（Layered Window）也画进结果里。
    /// </summary>
    /// <remarks>
    /// 默认<strong>不</strong>启用：MortarHUD 自己的 HUD 就是一个分层窗口，
    /// 不带这个标志时它天然不会出现在截图里，也就不会污染 ROI 里的 OCR 输入。
    /// 极少数游戏自身用分层窗口渲染，那种情况下用户可以在设置里打开它。
    /// </remarks>
    internal const int CAPTUREBLT = 0x40000000;

    internal const uint BI_RGB = 0;

    [StructLayout(LayoutKind.Sequential)]
    internal struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
        public uint bmiColors0;
    }

    [LibraryImport("user32.dll")]
    internal static partial IntPtr GetDC(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    internal static partial int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    internal static partial IntPtr CreateCompatibleDC(IntPtr hdc);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DeleteDC(IntPtr hdc);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    internal static partial IntPtr CreateDIBSection(
        IntPtr hdc,
        ref BITMAPINFO pbmi,
        uint usage,
        out IntPtr ppvBits,
        IntPtr hSection,
        uint offset);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    internal static partial IntPtr SelectObject(IntPtr hdc, IntPtr hObject);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DeleteObject(IntPtr hObject);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool BitBlt(
        IntPtr hdcDest, int xDest, int yDest, int width, int height,
        IntPtr hdcSrc, int xSrc, int ySrc, int rop);
}
