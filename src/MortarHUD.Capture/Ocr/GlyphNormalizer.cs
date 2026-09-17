using OpenCvSharp;

namespace MortarHUD.Capture.Ocr;

/// <summary>
/// 把任意大小的字形裁剪、缩放、居中到统一尺寸，让模板可以逐像素比较。
/// </summary>
/// <remarks>
/// 关键是<strong>保持长宽比</strong>：字母 i 和 m 的宽高比完全不同，
/// 拉伸填满会把它们变得一样宽，匹配就失去意义了。这里按短边缩放后居中留黑边。
/// </remarks>
public static class GlyphNormalizer
{
    /// <summary>
    /// 归一化一张「白字黑底」的字形位图。
    /// </summary>
    /// <returns>尺寸恒为 <see cref="GlyphAtlas.TemplateWidth"/> × <see cref="GlyphAtlas.TemplateHeight"/>。</returns>
    public static Mat Normalize(Mat whiteOnBlack)
    {
        ArgumentNullException.ThrowIfNull(whiteOnBlack);

        var canvas = new Mat(
            new Size(GlyphAtlas.TemplateWidth, GlyphAtlas.TemplateHeight),
            MatType.CV_8UC1,
            Scalar.All(0));

        var bounds = FindContentBounds(whiteOnBlack);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return canvas;
        }

        using var cropped = new Mat(whiteOnBlack, bounds);

        var scale = Math.Min(
            (double)GlyphAtlas.TemplateWidth / bounds.Width,
            (double)GlyphAtlas.TemplateHeight / bounds.Height);

        var targetWidth = Math.Max(1, (int)Math.Round(bounds.Width * scale));
        var targetHeight = Math.Max(1, (int)Math.Round(bounds.Height * scale));

        // 缩小用 Area（抗混叠），放大用 Linear（保持笔画平滑）。
        var interpolation = scale < 1.0 ? InterpolationFlags.Area : InterpolationFlags.Linear;

        using var resized = new Mat();
        Cv2.Resize(cropped, resized, new Size(targetWidth, targetHeight), 0, 0, interpolation);

        var offsetX = (GlyphAtlas.TemplateWidth - targetWidth) / 2;
        var offsetY = (GlyphAtlas.TemplateHeight - targetHeight) / 2;

        using var destination = new Mat(
            canvas, new Rect(offsetX, offsetY, targetWidth, targetHeight));

        resized.CopyTo(destination);
        return canvas;
    }

    /// <summary>去掉四周全黑的边，返回内容的紧致包围盒。</summary>
    public static Rect FindContentBounds(Mat whiteOnBlack)
    {
        Cv2.FindContours(
            whiteOnBlack,
            out var contours,
            out _,
            RetrievalModes.External,
            ContourApproximationModes.ApproxSimple);

        if (contours.Length == 0)
        {
            return new Rect(0, 0, 0, 0);
        }

        var all = new Rect(0, 0, 0, 0);
        var first = true;

        foreach (var contour in contours)
        {
            var box = Cv2.BoundingRect(contour);
            if (first)
            {
                all = box;
                first = false;
            }
            else
            {
                all = Union(all, box);
            }
        }

        return all;
    }

    private static Rect Union(Rect a, Rect b)
    {
        var left = Math.Min(a.Left, b.Left);
        var top = Math.Min(a.Top, b.Top);
        var right = Math.Max(a.Right, b.Right);
        var bottom = Math.Max(a.Bottom, b.Bottom);

        return new Rect(left, top, right - left, bottom - top);
    }

    /// <summary>
    /// 两张归一化字形之间的相似度，0..1，1 表示逐像素相同。
    /// </summary>
    /// <remarks>
    /// 用平均绝对差而不是相关系数：字形是同一个字体的同一次光栅化，
    /// 差别主要来自抗锯齿和亚像素位移，绝对差对这类差异的刻画更直接，
    /// 也不会像相关系数那样在「几乎全黑」的图上产生虚高的分数。
    /// </remarks>
    public static double Similarity(Mat a, Mat b)
    {
        if (a.Size() != b.Size())
        {
            throw new ArgumentException("两张字形图的尺寸必须一致。", nameof(b));
        }

        using var difference = new Mat();
        Cv2.Absdiff(a, b, difference);

        var meanDifference = Cv2.Mean(difference).Val0;
        return Math.Clamp(1.0 - meanDifference / 255.0, 0.0, 1.0);
    }
}
