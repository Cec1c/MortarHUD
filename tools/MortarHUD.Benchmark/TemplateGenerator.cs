using System.Globalization;
using System.Text;
using MortarHUD.Capture.ImageProcessing;
using MortarHUD.Capture.Ocr;
using MortarHUD.Capture.ScreenCapture;
using OpenCvSharp;
using Rectangle = System.Drawing.Rectangle;

namespace MortarHUD.Benchmark;

/// <summary>
/// 从实机截图里学习字形模板（TDD §6 的「自定义数字模板识别」）。
/// </summary>
/// <remarks>
/// <para>
/// 思路是利用 fixture 已经标注了期望文本这一点：把文字区域切片、分割成字形之后，
/// 只要某一行切出来的字形个数<em>正好等于</em>期望字符串的长度，
/// 就可以按从左到右的顺序把字形和字符一一对上，自动完成标注。
/// </para>
/// <para>
/// 个数对不上的行直接跳过——那说明有字形粘连或者混进了地图噪点，
/// 强行对齐会把错误标签写进模板库。
/// </para>
/// <para>
/// 每条预处理流水线各生成一套变体：不同二值化算法产出的笔画粗细差别很大，
/// 一份模板覆盖不了（详见 <see cref="GlyphAtlas"/> 的说明）。
/// </para>
/// </remarks>
public static class TemplateGenerator
{
    /// <summary>同一个字符的样本之间至少要有多像，低于这个值就当作对齐配错了。</summary>
    private const double MinimumSampleAgreement = 0.80;

    public sealed record Result(
        IReadOnlyList<GlyphTemplate> Entries,
        IReadOnlyList<Mat> Templates,
        int LinesUsed,
        int LinesSkipped);

    public static Result Generate(
        FixtureManifest manifest,
        string screenshotDirectory,
        string outputDirectory,
        IReadOnlyList<IImagePreprocessor> preprocessors)
    {
        // (字符, 流水线) → 归一化位图样本
        var samples = new Dictionary<(string Label, string Pipeline), List<Mat>>();
        var linesUsed = 0;
        var skipped = new List<string>();

        foreach (var fixture in manifest.Fixtures)
        {
            var screenshotPath = Path.Combine(screenshotDirectory, fixture.Screenshot);
            if (!File.Exists(screenshotPath))
            {
                skipped.Add($"{fixture.Id}: 缺少截图");
                continue;
            }

            if (fixture.LabelBounds is not { } labelBounds)
            {
                skipped.Add($"{fixture.Id}: manifest 里没有 labelBounds");
                continue;
            }

            using var full = Cv2.ImRead(screenshotPath, ImreadModes.Color);
            if (full.Empty())
            {
                skipped.Add($"{fixture.Id}: 截图读取失败");
                continue;
            }

            // 只在实测的文字区域内切字形。整块 ROI 上有大量地图纹理
            // （实测 46 个连通域里只有 13 个是字形），直接切会污染模板库。
            var bounds = Rectangle.Intersect(
                new Rectangle(labelBounds.X, labelBounds.Y, labelBounds.Width, labelBounds.Height),
                new Rectangle(0, 0, full.Width, full.Height));

            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                skipped.Add($"{fixture.Id}: labelBounds 落在截图之外");
                continue;
            }

            using var roi = new Mat(full, new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height));

            foreach (var preprocessor in preprocessors)
            {
                using var processed = preprocessor.Process(roi);
                var expectedLines = BuildExpectedLines(fixture);
                var segments = FilterGlyphCandidates(GlyphSegmenter.Segment(processed));

                var lines = SplitIntoTwoLines(segments);
                if (lines is null)
                {
                    skipped.Add($"{fixture.Id}/{preprocessor.Name}: 无法切成两行（{segments.Count} 个连通域）");
                    continue;
                }

                for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
                {
                    var glyphs = lines[lineIndex];
                    var expected = expectedLines[lineIndex];

                    if (glyphs.Count != expected.Length)
                    {
                        skipped.Add(
                            $"{fixture.Id}/{preprocessor.Name} 第{lineIndex + 1}行: "
                            + $"字形数 {glyphs.Count} != 期望 {expected.Length}（期望 \"{expected}\"）");
                        continue;
                    }

                    for (var i = 0; i < glyphs.Count; i++)
                    {
                        var key = (expected[i].ToString(), preprocessor.Name);
                        var normalized = GlyphNormalizer.Normalize(glyphs[i].Bitmap);

                        if (!samples.TryGetValue(key, out var list))
                        {
                            list = [];
                            samples[key] = list;
                        }

                        list.Add(normalized);
                    }

                    linesUsed++;
                }
            }
        }

        var droppedSamples = DropInconsistentSamples(samples);

        // 固定顺序输出，保证生成结果可复现（也方便 diff 雪碧图）。
        var keys = samples.Keys
            .Where(k => samples[k].Count > 0)
            .OrderBy(k => k.Label switch { "x" => 0, "y" => 1, "." => 2, _ => 3 })
            .ThenBy(k => k.Label, StringComparer.Ordinal)
            .ThenBy(k => k.Pipeline, StringComparer.Ordinal)
            .ToList();

        var entries = keys.Select(k => new GlyphTemplate(k.Label, k.Pipeline)).ToList();
        var templates = keys.Select(k => Average(samples[k])).ToList();

        Console.WriteLine();
        Console.WriteLine($"  覆盖字符   : {(entries.Count == 0 ? "（空）" : string.Concat(
            entries.Select(e => e.Label).Distinct(StringComparer.Ordinal).OrderBy(l => l, StringComparer.Ordinal)))}");
        Console.WriteLine($"  模板条数   : {entries.Count}（字符 × 流水线）");
        Console.WriteLine($"  使用行数   : {linesUsed}");
        Console.WriteLine($"  剔除样本   : {droppedSamples}");

        foreach (var entry in entries)
        {
            Console.WriteLine($"    '{entry.Label}' @流水线{entry.Pipeline} : {samples[(entry.Label, entry.Pipeline)].Count} 个样本");
        }

        if (skipped.Count > 0)
        {
            Console.WriteLine("  已跳过的行：");
            foreach (var reason in skipped)
            {
                Console.WriteLine($"    - {reason}");
            }
        }

        var covered = entries.Select(e => e.Label).ToHashSet(StringComparer.Ordinal);
        var missingDigits = "0123456789".Where(d => !covered.Contains(d.ToString())).ToList();
        if (missingDigits.Count > 0)
        {
            Console.WriteLine($"  注意：这些数字没有出现在任何可用样本里，模板库不覆盖："
                              + string.Join(", ", missingDigits));
        }

        if (entries.Count == 0)
        {
            throw new InvalidOperationException(
                "没有学到任何字形模板。请先看上面的「已跳过的行」，"
                + "通常是字形粘连或噪点导致分割数量与期望文本长度不一致。");
        }

        GlyphAtlas.Save(outputDirectory, entries, templates);
        Console.WriteLine($"  模板库已写入：{outputDirectory}");

        foreach (var list in samples.Values)
        {
            foreach (var mat in list)
            {
                mat.Dispose();
            }
        }

        return new Result(entries, templates, linesUsed, skipped.Count);
    }

    /// <summary>
    /// 剔除与同（字符 × 流水线）其它样本明显不一致的样本。
    /// </summary>
    /// <remarks>
    /// 同一个字符是同一套字体的同一次光栅化，样本之间应该高度一致。
    /// 一致性差的样本几乎一定是自动对齐时配错了（把噪点认成了某个字符），
    /// 留着会把模板平均歪。宁可少一个样本，也不要脏一个模板。
    /// </remarks>
    private static int DropInconsistentSamples(Dictionary<(string Label, string Pipeline), List<Mat>> samples)
    {
        var dropped = 0;

        foreach (var key in samples.Keys.ToList())
        {
            var list = samples[key];
            if (list.Count < 2)
            {
                continue;
            }

            var reference = list[0];
            var outliers = list
                .Skip(1)
                .Where(sample => GlyphNormalizer.Similarity(sample, reference) < MinimumSampleAgreement)
                .ToList();

            foreach (var outlier in outliers)
            {
                list.Remove(outlier);
                dropped++;
            }
        }

        return dropped;
    }

    /// <summary>
    /// 按尺寸把明显不是字形的东西滤掉。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 实测（放大 3 倍后的像素尺寸）：数字 h≈31~32 / w≈17~20，
    /// 小写 <c>x</c> 因为只有 x-height 所以 h≈22，小数点 h≈4 / w≈3。
    /// 混进来的地图标记方括号、道路碎片高度落在 5~26 之间，
    /// 并且不符合「细长数字」或「小方点」这两种形态。
    /// </para>
    /// <para>
    /// 判据用的是<em>相对</em>字高（模态值），换分辨率或换缩放倍数都不用改常量。
    /// </para>
    /// </remarks>
    private static List<SegmentedGlyph> FilterGlyphCandidates(List<SegmentedGlyph> segments)
    {
        if (segments.Count == 0)
        {
            return segments;
        }

        // 模态高度 = 出现最多的那个高度，也就是数字的高度。
        var modalHeight = segments
            .GroupBy(s => s.Bounds.Height)
            .OrderByDescending(g => g.Count())
            .ThenByDescending(g => g.Key)
            .First()
            .Key;

        var minimumGlyphHeight = modalHeight * 0.70;  // 容得下 x-height 的 'x'

        // 小数点判据不能收得太紧：实测 3x 放大后小数点高约 6px，
        // 按 0.15×字高（≈4.7）算会把它误杀，导致每行都少一个字形、整行对不齐。
        var maximumDotHeight = modalHeight * 0.28;
        var maximumDotWidth = modalHeight * 0.40;

        return segments.Where(s =>
        {
            var height = s.Bounds.Height;
            var width = s.Bounds.Width;

            if (height >= minimumGlyphHeight)
            {
                return true;
            }

            // 小数点：又矮又小，而且必须是个小方块。
            return height <= maximumDotHeight && width <= maximumDotWidth;
        }).ToList();
    }

    /// <summary>
    /// 把字形按「最大的纵向空隙」切成上下两组。
    /// </summary>
    /// <remarks>
    /// 坐标读数固定是两行，两行间距（放大后约 165px）远大于行内的字形间距（0~3px），
    /// 所以最大的那个空隙一定就是行间空隙。这比通用的行聚类稳得多——
    /// 通用聚类会被零星的地图噪点带偏。
    /// </remarks>
    private static List<List<SegmentedGlyph>>? SplitIntoTwoLines(List<SegmentedGlyph> segments)
    {
        if (segments.Count < 2)
        {
            return null;
        }

        var ordered = segments.OrderBy(s => s.CenterY).ToList();

        var splitIndex = -1;
        var largestGap = 0.0;

        for (var i = 1; i < ordered.Count; i++)
        {
            var gap = ordered[i].CenterY - ordered[i - 1].CenterY;
            if (gap > largestGap)
            {
                largestGap = gap;
                splitIndex = i;
            }
        }

        if (splitIndex <= 0 || splitIndex >= ordered.Count)
        {
            return null;
        }

        return
        [
            ordered.Take(splitIndex).OrderBy(s => s.Bounds.Left).ToList(),
            ordered.Skip(splitIndex).OrderBy(s => s.Bounds.Left).ToList(),
        ];
    }

    /// <summary>fixture 的期望文本按行拆开：第一行是 y，第二行是 x。</summary>
    private static List<string> BuildExpectedLines(FixtureCase fixture)
    {
        var y = fixture.ExpectedY.ToString("0.00", CultureInfo.InvariantCulture);
        var x = fixture.ExpectedX.ToString("0.00", CultureInfo.InvariantCulture);

        return [$"y{y}", $"x{x}"];
    }

    /// <summary>把同一（字符 × 流水线）的多个样本平均成一张模板。</summary>
    private static Mat Average(List<Mat> samples)
    {
        var accumulator = new Mat(
            new Size(GlyphAtlas.TemplateWidth, GlyphAtlas.TemplateHeight),
            MatType.CV_32FC1,
            Scalar.All(0));

        foreach (var sample in samples)
        {
            using var asFloat = new Mat();
            sample.ConvertTo(asFloat, MatType.CV_32FC1);

            using var sum = new Mat();
            Cv2.Add(accumulator, asFloat, sum);
            sum.CopyTo(accumulator);
        }

        var average = new Mat();
        accumulator.ConvertTo(average, MatType.CV_8UC1, 1.0 / samples.Count);
        accumulator.Dispose();

        return average;
    }

    /// <summary>把模板库渲染成一张便于肉眼检查的概览图。</summary>
    public static void WriteContactSheet(Result result, string path)
    {
        const int scale = 6;
        const int padding = 8;

        if (result.Entries.Count == 0)
        {
            return;
        }

        var cellWidth = GlyphAtlas.TemplateWidth * scale + padding;
        var cellHeight = GlyphAtlas.TemplateHeight * scale + padding;

        using var sheet = new Mat(
            new Size(cellWidth * result.Entries.Count + padding, cellHeight + padding),
            MatType.CV_8UC1,
            Scalar.All(255));

        for (var i = 0; i < result.Entries.Count; i++)
        {
            using var big = new Mat();
            Cv2.Resize(result.Templates[i], big,
                new Size(GlyphAtlas.TemplateWidth * scale, GlyphAtlas.TemplateHeight * scale),
                0, 0, InterpolationFlags.Nearest);

            var target = new Rect(padding + i * cellWidth, padding, big.Width, big.Height);
            using var view = new Mat(sheet, target);
            big.CopyTo(view);
        }

        Cv2.ImWrite(path, sheet);

        Console.WriteLine($"  模板概览图：{path}");
        Console.WriteLine("  顺序：" + string.Join(" ",
            result.Entries.Select(e => $"{e.Label}@{e.Pipeline}")));
    }
}
