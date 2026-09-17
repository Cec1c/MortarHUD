using OpenCvSharp;

namespace MortarHUD.Capture.Ocr;

/// <summary>一个切出来的字形候选，带它在原图里的位置。</summary>
public sealed record SegmentedGlyph(Mat Bitmap, Rect Bounds)
{
    public int CenterY => Bounds.Top + Bounds.Height / 2;
}

/// <summary>
/// 把「黑字白底」的预处理图切成一个个字形。
/// </summary>
/// <remarks>
/// 模板生成工具和运行时引擎共用这一份实现——两边如果各写一套分割逻辑，
/// 生成出来的模板和运行时切出来的字形就会对不上，匹配分数会莫名其妙地低。
/// </remarks>
public static class GlyphSegmenter
{
    /// <summary>小于这么多像素的连通域当噪点丢弃。</summary>
    public const int MinimumComponentArea = 8;

    /// <summary>超过图像高度这个比例的连通域当图形元素丢弃（地图上的路、图标等）。</summary>
    public const double MaximumComponentHeightRatio = 0.45;

    public static List<SegmentedGlyph> Segment(Mat blackTextOnWhite)
    {
        ArgumentNullException.ThrowIfNull(blackTextOnWhite);

        // 统一转成「白字黑底」再找连通域，前景为 255。
        using var whiteOnBlack = new Mat();
        if (blackTextOnWhite.Channels() == 1)
        {
            Cv2.BitwiseNot(blackTextOnWhite, whiteOnBlack);
        }
        else
        {
            using var gray = new Mat();
            Cv2.CvtColor(blackTextOnWhite, gray, ColorConversionCodes.BGR2GRAY);
            Cv2.BitwiseNot(gray, whiteOnBlack);
        }

        using var binary = new Mat();
        Cv2.Threshold(whiteOnBlack, binary, 127, 255, ThresholdTypes.Binary);

        // OpenCvSharp 里这三个参数是输出用的 Mat，不是 out 参数。
        using var labels = new Mat();
        using var stats = new Mat();
        using var centroids = new Mat();

        var componentCount = Cv2.ConnectedComponentsWithStats(
            binary, labels, stats, centroids, PixelConnectivity.Connectivity8);

        var glyphs = new List<SegmentedGlyph>();
        var maxHeight = whiteOnBlack.Height * MaximumComponentHeightRatio;

        // 第 0 个是背景，从 1 开始。
        for (var i = 1; i < componentCount; i++)
        {
            var area = stats.At<int>(i, (int)ConnectedComponentsTypes.Area);
            var left = stats.At<int>(i, (int)ConnectedComponentsTypes.Left);
            var top = stats.At<int>(i, (int)ConnectedComponentsTypes.Top);
            var width = stats.At<int>(i, (int)ConnectedComponentsTypes.Width);
            var height = stats.At<int>(i, (int)ConnectedComponentsTypes.Height);

            if (area < MinimumComponentArea || height > maxHeight || width <= 0 || height <= 0)
            {
                continue;
            }

            // 只保留这一个连通域，避免把旁边的地图噪点一起带进模板比较。
            using var indexMat = new Mat(labels.Size(), labels.Type(), Scalar.All(i));
            using var componentMask = new Mat();
            Cv2.Compare(labels, indexMat, componentMask, CmpType.EQ);

            using var isolated = new Mat();
            Cv2.BitwiseAnd(whiteOnBlack, componentMask, isolated);

            using var cropped = new Mat(isolated, new Rect(left, top, width, height));
            glyphs.Add(new SegmentedGlyph(cropped.Clone(), new Rect(left, top, width, height)));
        }

        return glyphs;
    }

    /// <summary>
    /// 按纵向重叠把字形分组成行，行内按 x 排序。
    /// </summary>
    /// <remarks>
    /// 坐标读数固定是两行（y 一行、x 一行）。分组是为了让输出文本里
    /// 两行之间有个换行；Parser 本身不依赖行结构。
    /// </remarks>
    public static List<List<SegmentedGlyph>> GroupIntoLines(
        List<SegmentedGlyph> glyphs, int imageHeight)
    {
        ArgumentNullException.ThrowIfNull(glyphs);

        if (glyphs.Count == 0)
        {
            return [];
        }

        var ordered = glyphs.OrderBy(g => g.Bounds.Top).ToList();

        // 容差基于「众数字高」而不是中位字高。
        //
        // 这一点是实测逼出来的：ROI 里除了文字还有大量细小的地图噪点，
        // 中位数被它们拉得很低，容差随之变小，于是带下伸部分的 'y'（中心比数字低约 10px）
        // 会被判成单独一行，读数被拆散成 "10978.?1." 和 "y." 两段，整行报废。
        // 众数反映的是真正的字符高度，不会被零星噪点带偏。
        var modalHeight = ordered
            .GroupBy(g => g.Bounds.Height)
            .OrderByDescending(g => g.Count())
            .ThenByDescending(g => g.Key)
            .First()
            .Key;

        var lineTolerance = Math.Max(4.0, modalHeight * 0.8);
        var lines = new List<(double Center, List<SegmentedGlyph> Items)>();

        foreach (var glyph in ordered)
        {
            var center = (double)glyph.CenterY;
            var index = lines.FindIndex(l => Math.Abs(l.Center - center) <= lineTolerance);

            if (index < 0)
            {
                lines.Add((center, [glyph]));
            }
            else
            {
                var items = lines[index].Items;
                items.Add(glyph);

                // 用行内平均中心更新，避免整行被第一个字形的位置带偏。
                lines[index] = (items.Average(i => (double)i.CenterY), items);
            }
        }

        return lines
            .OrderBy(l => l.Center)
            .Select(l => l.Items.OrderBy(i => i.Bounds.Left).ToList())
            .ToList();
    }
}
