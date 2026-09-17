using OpenCvSharp;

namespace MortarHUD.Capture.ScreenCapture;

/// <summary>
/// 截取屏幕上的一块矩形区域。
/// </summary>
/// <remarks>
/// <para>
/// 刻意<strong>不</strong>写死具体技术（TDD §11）：v0.1 用 GDI BitBlt，
/// 如果某些游戏模式抓不到，再加一个 DXGI Desktop Duplication 实现即可，
/// 上层代码一行都不用改。
/// </para>
/// <para>
/// 返回类型是 OpenCV 的 <see cref="Mat"/> 而不是 TDD 草稿里的 <c>Bitmap</c>：
/// OCR 引擎的入参就是 <c>Mat</c>（TDD §13），统一成一种图像类型可以省掉
/// 捕获与识别之间的一次格式转换，也避免把 System.Drawing.Common 拖进来。
/// 坐标语义与「provider 可替换」这两条约束都没有改变。
/// </para>
/// <para>所有坐标都是<strong>物理像素</strong>（TDD §12）。</para>
/// </remarks>
public interface IScreenCaptureProvider : IDisposable
{
    /// <summary>
    /// 截取指定矩形。
    /// </summary>
    /// <param name="physicalPixelRect">
    /// 虚拟桌面坐标系下的物理像素矩形。调用方负责保证它落在屏幕范围内。
    /// </param>
    /// <returns>BGRA 三通道（BGR）图像，尺寸与矩形一致。</returns>
    /// <exception cref="ScreenCaptureException">捕获失败。</exception>
    Mat Capture(System.Drawing.Rectangle physicalPixelRect);
}

/// <summary>屏幕捕获失败。</summary>
public sealed class ScreenCaptureException : Exception
{
    public ScreenCaptureException(string message) : base(message)
    {
    }

    public ScreenCaptureException(string message, Exception inner) : base(message, inner)
    {
    }
}
