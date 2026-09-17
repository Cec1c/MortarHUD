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

        // Auto：能用 Tesseract 就用，否则退回模板引擎。
        var tesseract = new TesseractOcrEngine(settings, tessdataPath);
        if (tesseract.IsAvailable)
        {
            return [tesseract];
        }

        tesseract.Dispose();
        return [CreateTemplate()];
    }

    /// <summary>Benchmark 用：把全部引擎都造出来横向比较。</summary>
    public static IReadOnlyList<ICoordinateOcrEngine> CreateAllForBenchmark(
        OcrSettings settings, string? tessdataPath = null)
        => [new TesseractOcrEngine(settings, tessdataPath), CreateTemplate()];

    private static ICoordinateOcrEngine CreateTemplate() => new TemplateOcrEngine();
}
