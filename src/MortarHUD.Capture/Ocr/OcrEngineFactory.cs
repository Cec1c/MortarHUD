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

    /// <summary>整块与稀疏分页相互校验。它们都可能误读，任何有效读数分歧都会失败。</summary>
    /// <remarks>
    /// 两种分页各自配合 AutoCandidates 的颜色投影；不能用票数掩盖某一条不同的读数。
    /// 用户显式选择 Tesseract 时，才使用设置中的单一 PageSegMode。
    /// </remarks>
    private static readonly int[] AutoPageSegModes = [6, 11];

    /// <summary>Benchmark 用：把全部引擎都造出来横向比较。</summary>
    public static IReadOnlyList<ICoordinateOcrEngine> CreateAllForBenchmark(
        OcrSettings settings, string? tessdataPath = null)
        => [new TesseractOcrEngine(settings, tessdataPath), CreateTemplate()];

    private static ICoordinateOcrEngine CreateTemplate() => new TemplateOcrEngine();
}
