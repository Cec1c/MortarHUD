using System.Text.Json;
using OpenCvSharp;

namespace MortarHUD.Benchmark;

/// <summary>一条采集记录里能用来做字形样本的部分。</summary>
public sealed class HarvestedRecord
{
    public required string Id { get; init; }

    /// <summary>截好的 ROI 图（<c>_raw.png</c>）。</summary>
    public required string RoiPath { get; init; }

    /// <summary>当时识别出的坐标——它就是这一行的「标准答案」，等于自带标注。</summary>
    public required double ExpectedX { get; init; }

    public required double ExpectedY { get; init; }

    /// <summary>
    /// 坐标文字在 <see cref="RoiPath"/> 里的包围盒。
    /// </summary>
    public required LabelBounds LabelBounds { get; init; }
}

/// <summary>
/// 从运行期的采集记录里挖字形样本。
/// </summary>
/// <remarks>
/// <para>
/// <c>%AppData%\MortarHUD\Debug\</c> 下的每条记录都存了当时的 ROI 和**识别成功的坐标**。
/// 后者等于自带标注：既然引擎读出了 110.34，那图上写的就一定是 110.34。
/// 于是不必人工量包围盒，就能把实机截图变成字形样本。
/// </para>
/// <para>
/// 这解决的是「fixture 太少」这个根本问题：仓库里只有 3 张 fixture，
/// 坐标数字的并集恰好缺 2 和 3，而实机采集里有几十条覆盖全部十位数字的样本。
/// </para>
/// <para>
/// 定位方式见 <see cref="LocateLabel"/>：坐标文字在 ROI 里是一小块高密度白字，
/// 且固定是「上排 y、下排 x」两行。这里只负责找到它，切字形仍交给
/// <see cref="TemplateGenerator"/> ——两边用同一套分割逻辑，模板才不会和运行时对不上。
/// </para>
/// <para>
/// 采集记录不入仓库（几十张截图约 1.4MB，且含实机画面）。
/// 字形库由 <c>--gen-from-debug</c> 生成，谁都能用自己的采集记录重跑一遍。
/// </para>
/// </remarks>
public static class DebugRecordHarvester
{
    /// <summary>采集记录的默认位置。</summary>
    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MortarHUD", "Debug");

    /// <summary>灰度阈值。文字是白的（实测 184~255），地图底色只有 60~120。</summary>
    private const int InkThreshold = 150;

    /// <summary>一行文字里至少要有这么多个字形才算数。</summary>
    private const int MinimumGlyphsPerLine = 4;

    /// <summary>两行之间的基线间距（实测 55px，容差放宽到 45~70）。</summary>
    private const int MinimumLineGap = 45;
    private const int MaximumLineGap = 70;

    public static IReadOnlyList<HarvestedRecord> Harvest(string debugDirectory)
    {
        if (!Directory.Exists(debugDirectory))
        {
            return [];
        }

        var records = new List<HarvestedRecord>();

        foreach (var jsonPath in Directory.GetFiles(debugDirectory, "*_result.json"))
        {
            var id = Path.GetFileName(jsonPath).Replace("_result.json", "");
            var roiPath = Path.Combine(debugDirectory, id + "_raw.png");

            if (!File.Exists(roiPath))
            {
                continue;
            }

            if (Parse(jsonPath) is not { } parsed)
            {
                continue;
            }

            using var roi = Cv2.ImRead(roiPath, ImreadModes.Color);
            if (roi.Empty())
            {
                continue;
            }

            var bounds = LocateLabel(roi, parsed.ExpectedY, parsed.ExpectedX);
            if (bounds is null)
            {
                continue;
            }

            records.Add(new HarvestedRecord
            {
                Id = id,
                RoiPath = roiPath,
                ExpectedX = parsed.ExpectedX,
                ExpectedY = parsed.ExpectedY,
                LabelBounds = bounds,
            });
        }

        return records;
    }

    private static (double ExpectedX, double ExpectedY)? Parse(string jsonPath)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(jsonPath));
            var root = document.RootElement;

            // 只有识别成功的记录才自带标注；失败的记录没有可信的标准答案。
            if (!root.TryGetProperty("coordinate", out var coordinate)
                || coordinate.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            return (coordinate.GetProperty("x").GetDouble(), coordinate.GetProperty("y").GetDouble());
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// 找出坐标文字在 ROI 里的包围盒。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 坐标文字是 ROI 里唯一「成行的高亮小字形」：白色、字高 11px、
    /// 上排 y 下排 x、两行间距 55px。地图上的网格线和地块边缘都比它大得多。
    /// </para>
    /// <para>
    /// 注意轴字母（<c>x</c>/<c>y</c>）会和紧邻的第一个数字粘成一个大连通域
    /// （实测出现过 32x31），所以不靠连通域个数判断，只要求「一行里有 4 个以上
    /// 尺寸像字形的团块」就接受，细节切分交给列投影。
    /// </para>
    /// </remarks>
    private static LabelBounds? LocateLabel(Mat roi, double expectedY, double expectedX)
    {
        using var gray = new Mat();
        Cv2.CvtColor(roi, gray, ColorConversionCodes.BGR2GRAY);

        var glyphLike = GlyphLikeComponents(gray);
        if (glyphLike.Count < MinimumGlyphsPerLine * 2)
        {
            return null;
        }

        var rows = GroupIntoRows(glyphLike);

        var candidates = rows
            .Where(r => r.Count >= MinimumGlyphsPerLine)
            .Where(r => r.Max(c => c.Right) - r.Min(c => c.Left) <= 90)
            .OrderBy(r => r.Average(c => c.Y))
            .ToList();

        for (var i = 0; i < candidates.Count; i++)
        {
            for (var j = i + 1; j < candidates.Count; j++)
            {
                var gap = candidates[j].Average(c => c.Y) - candidates[i].Average(c => c.Y);
                if (gap is < MinimumLineGap or > MaximumLineGap)
                {
                    continue;
                }

                // 上排是 y 行、下排是 x 行。
                var upper = Union(candidates[i]);
                var lower = Union(candidates[j]);
                var left = Math.Min(upper.Left, lower.Left);
                var top = Math.Min(upper.Top, lower.Top);
                var right = Math.Max(upper.Right, lower.Right);
                var bottom = Math.Max(upper.Bottom, lower.Bottom);

                return new LabelBounds
                {
                    X = left,
                    Y = top,
                    Width = right - left,
                    Height = bottom - top,
                };
            }
        }

        return null;
    }

    /// <summary>找出尺寸像坐标文字的字形团块（数字高 11、宽 3~8，小数点 2x2）。</summary>
    private static List<Rect> GlyphLikeComponents(Mat gray)
    {
        using var mask = new Mat();
        Cv2.Threshold(gray, mask, InkThreshold, 255, ThresholdTypes.Binary);

        using var labels = new Mat();
        using var stats = new Mat();
        using var centroids = new Mat();
        var count = Cv2.ConnectedComponentsWithStats(
            mask, labels, stats, centroids, PixelConnectivity.Connectivity8);

        var result = new List<Rect>();

        for (var i = 1; i < count; i++)
        {
            var width = stats.At<int>(i, (int)ConnectedComponentsTypes.Width);
            var height = stats.At<int>(i, (int)ConnectedComponentsTypes.Height);

            // 数字/字母：高 8~15。宽放过粘连（上限 12），横竖条（网格线）由宽高比排除。
            if (height is >= 8 and <= 15 && width <= 12)
            {
                result.Add(new Rect(
                    stats.At<int>(i, (int)ConnectedComponentsTypes.Left),
                    stats.At<int>(i, (int)ConnectedComponentsTypes.Top),
                    width,
                    height));

                continue;
            }

            // 小数点：2~4 见方的小亮点。
            if (height is >= 1 and <= 5 && width is >= 1 and <= 5)
            {
                var area = stats.At<int>(i, (int)ConnectedComponentsTypes.Area);
                if (area >= 2)
                {
                    result.Add(new Rect(
                        stats.At<int>(i, (int)ConnectedComponentsTypes.Left),
                        stats.At<int>(i, (int)ConnectedComponentsTypes.Top),
                        width,
                        height));
                }
            }
        }

        return result;
    }

    private static List<List<Rect>> GroupIntoRows(List<Rect> glyphs)
    {
        var rows = new List<List<Rect>>();

        foreach (var glyph in glyphs.OrderBy(g => g.Y))
        {
            var row = rows.FirstOrDefault(r => Math.Abs(r[0].Y - glyph.Y) <= 4);

            if (row is null)
            {
                rows.Add([glyph]);
            }
            else
            {
                row.Add(glyph);
            }
        }

        return rows;
    }

    private static Rect Union(List<Rect> rects)
    {
        var left = rects.Min(r => r.Left);
        var top = rects.Min(r => r.Top);
        var right = rects.Max(r => r.Right);
        var bottom = rects.Max(r => r.Bottom);

        return new Rect(left, top, right - left, bottom - top);
    }
}
