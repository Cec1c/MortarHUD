using System.Runtime.InteropServices;
using MortarHUD.Localization;
using MortarHUD.Platform.Windows.NativeMethods;
using OpenCvSharp;

namespace MortarHUD.Capture.ScreenCapture;

/// <summary>
/// 用 GDI <c>BitBlt</c> 抓屏（TDD §11 的 v0.1 方案）。
/// </summary>
/// <remarks>
/// <para>
/// 只在按下热键时截一块很小的 ROI，不需要 60 FPS 连续捕获，
/// 所以 BitBlt 的性能完全够用，而且兼容性最好。
/// </para>
/// <para>
/// 实现方式是 BitBlt 到一块 32bpp 的 DIB Section，再把它包成 <see cref="Mat"/>。
/// 相比 GDI+ 的 <c>Graphics.CopyFromScreen</c>，省掉了一次整块内存拷贝。
/// </para>
/// </remarks>
public sealed class GdiScreenCaptureProvider : IScreenCaptureProvider
{
    private readonly bool _includeLayeredWindows;
    private readonly object _sync = new();

    private bool _disposed;

    /// <param name="includeLayeredWindows">
    /// 是否把分层窗口也画进截图。
    /// 默认 false —— MortarHUD 自己的 HUD 就是分层窗口，默认排除它，
    /// 这样即使 HUD 恰好压在 ROI 上也不会污染 OCR 输入。
    /// </param>
    public GdiScreenCaptureProvider(bool includeLayeredWindows = false)
        => _includeLayeredWindows = includeLayeredWindows;

    public Mat Capture(System.Drawing.Rectangle physicalPixelRect)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (physicalPixelRect.Width <= 0 || physicalPixelRect.Height <= 0)
        {
            throw new ScreenCaptureException(Loc.F("InvalidROISizeX", physicalPixelRect.Width, physicalPixelRect.Height));
        }

        lock (_sync)
        {
            return CaptureCore(physicalPixelRect);
        }
    }

    private Mat CaptureCore(System.Drawing.Rectangle rect)
    {
        var screenDc = IntPtr.Zero;
        var memoryDc = IntPtr.Zero;
        var dibSection = IntPtr.Zero;
        var previousObject = IntPtr.Zero;

        try
        {
            screenDc = Gdi32.GetDC(IntPtr.Zero);
            if (screenDc == IntPtr.Zero)
            {
                throw new ScreenCaptureException(Loc.T("GetDCDesktopFailedTheScreenIsNotAccessible"));
            }

            memoryDc = Gdi32.CreateCompatibleDC(screenDc);
            if (memoryDc == IntPtr.Zero)
            {
                throw new ScreenCaptureException(
                    $"CreateCompatibleDC 失败（Win32 错误 {Marshal.GetLastWin32Error()}）。");
            }

            var bitmapInfo = new Gdi32.BITMAPINFO
            {
                bmiHeader = new Gdi32.BITMAPINFOHEADER
                {
                    biSize = (uint)Marshal.SizeOf<Gdi32.BITMAPINFOHEADER>(),
                    biWidth = rect.Width,
                    // 负高度 = 自上而下的 DIB，第 0 行就是屏幕最上面一行，
                    // 与 Mat 的行序一致，省掉一次上下翻转。
                    biHeight = -rect.Height,
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = Gdi32.BI_RGB,
                    biSizeImage = 0,
                },
            };

            dibSection = Gdi32.CreateDIBSection(
                screenDc, ref bitmapInfo, 0, out var bits, IntPtr.Zero, 0);

            if (dibSection == IntPtr.Zero || bits == IntPtr.Zero)
            {
                throw new ScreenCaptureException(
                    $"CreateDIBSection 失败（Win32 错误 {Marshal.GetLastWin32Error()}）。");
            }

            previousObject = Gdi32.SelectObject(memoryDc, dibSection);

            var rop = Gdi32.SRCCOPY | (_includeLayeredWindows ? Gdi32.CAPTUREBLT : 0);

            if (!Gdi32.BitBlt(
                    memoryDc, 0, 0, rect.Width, rect.Height,
                    screenDc, rect.Left, rect.Top, rop))
            {
                throw new ScreenCaptureException(
                    $"BitBlt 失败（Win32 错误 {Marshal.GetLastWin32Error()}）。"
                    + Loc.T("ScreenCaptureMayNotWorkInExclusiveFullscreenUseB"));
            }

            // DIB 是 BGRA，OCR 只关心灰度信息，转成 BGR 去掉 alpha 通道。
            using var bgra = Mat.FromPixelData(rect.Height, rect.Width, MatType.CV_8UC4, bits);
            var bgr = new Mat();
            Cv2.CvtColor(bgra, bgr, ColorConversionCodes.BGRA2BGR);
            return bgr;
        }
        catch (ScreenCaptureException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ScreenCaptureException(Loc.T("UnexpectedErrorDuringScreenCapture"), ex);
        }
        finally
        {
            if (memoryDc != IntPtr.Zero && previousObject != IntPtr.Zero)
            {
                Gdi32.SelectObject(memoryDc, previousObject);
            }

            if (dibSection != IntPtr.Zero)
            {
                Gdi32.DeleteObject(dibSection);
            }

            if (memoryDc != IntPtr.Zero)
            {
                Gdi32.DeleteDC(memoryDc);
            }

            if (screenDc != IntPtr.Zero)
            {
                Gdi32.ReleaseDC(IntPtr.Zero, screenDc);
            }
        }
    }

    public void Dispose()
    {
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
