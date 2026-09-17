using MortarHUD.Core.Configuration;

namespace MortarHUD.Capture.Ocr;

/// <summary>按设置造 OCR 引擎（TDD §32 的 OCR Engine 下拉框）。</summary>
public static class OcrEngineFactory
{
    /// <summary>
    /// Auto 模式的优先顺序。Tesseract 在字符集覆盖上更完整（10 个数字都有），
    /// 模板引擎作为「没有语言包 / Tesseract 起不来」时的兜底。
    /// </summary>
    public static IReadOnlyList<ICoordinateOcrEngine> Resolve(
        string? engineName, OcrSettings settings, string? tessdataPath = null)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (string.Equals(engineName, OcrEngineNames.Template, StringComparison.OrdinalIgnoreCase))
        {
            return [CreateTemplate()];
        }

        if (string.Equals(engineName, OcrEngineNames.Tesseract, StringComparison.OrdinalIgnoreCase))
        {
            return [new TesseractOcrEngine(settings, tessdataPath)];
        }

        // Auto：Tesseract 起得来就用集成，否则退回模板引擎。
        var engines = AutoPageSegModes
            .Select(psm =>
            {
                var segmentation = settings.Clone();
                segmentation.PageSegMode = psm;
                return (ICoordinateOcrEngine)new TesseractOcrEngine(segmentation, tessdataPath);
            })
            .ToList();

        if (engines[0] is TesseractOcrEngine { IsAvailable: false })
        {
            foreach (var engine in engines)
            {
                engine.Dispose();
            }

            return [CreateTemplate()];
        }

        return engines;
    }

    /// <summary>
    /// Auto 集成里用的分页模式。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 分页模式在这里不是「用户偏好」而是<strong>集成成员</strong>：两种模式各有软肋，
    /// 而且错法互不重叠。
    /// </para>
    /// <list type="bullet">
    /// <item>6（单一文本块）会把「y 行在上、x 行在下」当成一个块统一切分，
    /// 偶发把末位数字读错（同一张图读成 99.75，真值 99.73）。</item>
    /// <item>11（稀疏文本）会偶发丢掉前导数字（读成 0.07，真值 110.07）。</item>
    /// </list>
    /// <para>
    /// 117 份实机采集回放：只跑 6 是 65 次成功 / 16 次两票矛盾；只跑 11 是 79 / 5；
    /// 两者一起跑再取多数，是 78 次成功 / <strong>0 次矛盾</strong>。
    /// 矛盾清零是关键——再没有「两条流水线各读一个值、只好整个放弃」的情况。
    /// </para>
    /// <para>
    /// 代价是每次采集多一遍 OCR（约 20ms）。用户显式选 Tesseract（而非 Auto）时
    /// 只跑主引擎一遍，此时 <see cref="OcrSettings.PageSegMode"/> 才起作用。
    /// </para>
    /// </remarks>
    private static readonly int[] AutoPageSegModes = [6, 11];

    /// <summary>Benchmark 用：把全部引擎都造出来横向比较。</summary>
    public static IReadOnlyList<ICoordinateOcrEngine> CreateAllForBenchmark(
        OcrSettings settings, string? tessdataPath = null)
        => [new TesseractOcrEngine(settings, tessdataPath), CreateTemplate()];

    private static ICoordinateOcrEngine CreateTemplate() => new TemplateOcrEngine();
}
