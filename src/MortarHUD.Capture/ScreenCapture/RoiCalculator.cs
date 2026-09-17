using MortarHUD.Core.Configuration;

namespace MortarHUD.Capture.ScreenCapture;

/// <summary>
/// 把「光标位置 + ROI 配置」换算成实际要截取的物理像素矩形。
/// </summary>
/// <remarks>
/// <para>
/// 纯函数，没有 Win32 依赖，因此可以被单元测试完整覆盖——
/// ROI 算错会导致 OCR 拿到错误的图，而那种错误在实机上极难定位。
/// </para>
/// <para>
/// 默认值与推导依据见 <see cref="RoiSettings"/> 的注释。
/// </para>
/// </remarks>
public static class RoiCalculator
{
    /// <summary>实测默认值所对应的参考分辨率高度。</summary>
    public const int ReferenceScreenHeight = 1080;

    /// <summary>按 <see cref="RoiSettings.AutoScale"/> 解析出实际使用的缩放系数。</summary>
    public static double ResolveScale(RoiSettings roi, int screenHeight)
    {
        ArgumentNullException.ThrowIfNull(roi);

        if (!roi.AutoScale)
        {
            return roi.Scale <= 0 ? 1.0 : roi.Scale;
        }

        if (screenHeight <= 0)
        {
            return 1.0;
        }

        return (double)screenHeight / ReferenceScreenHeight;
    }

    /// <summary>计算未裁剪的 ROI 矩形。</summary>
    public static System.Drawing.Rectangle ComputeRaw(
        int cursorX, int cursorY, RoiSettings roi, int screenHeight)
    {
        ArgumentNullException.ThrowIfNull(roi);

        var scale = ResolveScale(roi, screenHeight);

        var width = Math.Max(1, (int)Math.Round(roi.Width * scale));
        var height = Math.Max(1, (int)Math.Round(roi.Height * scale));
        var offsetX = (int)Math.Round(roi.OffsetX * scale);
        var offsetY = (int)Math.Round(roi.OffsetY * scale);

        return new System.Drawing.Rectangle(cursorX + offsetX, cursorY + offsetY, width, height);
    }

    /// <summary>
    /// 把矩形裁进给定的屏幕范围。
    /// </summary>
    /// <remarks>
    /// 光标贴着屏幕边缘时 ROI 会有一截跑到屏幕外，
    /// 那个区域 BitBlt 回来是黑的。裁掉之后返回的矩形就是「真正截到的内容」，
    /// Debug 面板画出来的框才和实际一致。
    /// </remarks>
    public static System.Drawing.Rectangle ClampTo(
        System.Drawing.Rectangle rect, System.Drawing.Rectangle bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return rect;
        }

        var left = Math.Max(rect.Left, bounds.Left);
        var top = Math.Max(rect.Top, bounds.Top);
        var right = Math.Min(rect.Right, bounds.Right);
        var bottom = Math.Min(rect.Bottom, bounds.Bottom);

        if (right <= left || bottom <= top)
        {
            // 完全在屏幕外——返回一个 1x1 的占位，让上层走「捕获失败」而不是崩溃。
            return new System.Drawing.Rectangle(left, top, 1, 1);
        }

        return new System.Drawing.Rectangle(left, top, right - left, bottom - top);
    }

    /// <summary>一步到位：算 ROI 并裁进屏幕范围。</summary>
    public static System.Drawing.Rectangle Compute(
        int cursorX,
        int cursorY,
        RoiSettings roi,
        int screenHeight,
        System.Drawing.Rectangle virtualScreen)
        => ClampTo(ComputeRaw(cursorX, cursorY, roi, screenHeight), virtualScreen);
}
