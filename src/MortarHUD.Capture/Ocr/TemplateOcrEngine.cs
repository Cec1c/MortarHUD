using System.Diagnostics;
using System.Text;
using MortarHUD.Core.Models;
using OpenCvSharp;

namespace MortarHUD.Capture.Ocr;

/// <summary>
/// 自定义字形模板匹配引擎：零外部依赖、零语言包的本地 OCR。
/// </summary>
/// <remarks>
/// <para>
/// 游戏 UI 文字是固定字体、固定字号的点阵渲染，同一个字符每次画出来几乎一模一样，
/// 因此逐像素的模板匹配在这个场景下比通用 OCR 更直接，也更快。
/// </para>
/// <para>
/// <strong>未知字形处理：</strong>匹配分数低于阈值的字形不会被<em>丢掉</em>，
/// 而是替换成 <c>?</c>。丢掉会静默改变数字——比如 7 没认出来，
/// 「x107.66」就变成「x10.66」，那是 7 个坐标单位的误差。
/// 插入一个非法字符则会让 Parser 直接判定失败，这是安全的那一侧。
/// </para>
/// <para>
/// 模板库覆盖范围见 <see cref="Coverage"/>。当前 fixture 集里没有出现过数字 2 和 3，
/// 所以这两个字符没有被覆盖——它们会被判成 <c>?</c> 进而让本次识别失败，
/// 而不是猜一个值出来。
/// </para>
/// </remarks>
public sealed class TemplateOcrEngine : ICoordinateOcrEngine
{
    /// <summary>低于这个相似度的字形视为「不认识」。</summary>
    private const double MatchThreshold = 0.70;

    private readonly GlyphAtlas? _atlas;
    private bool _disposed;

    public TemplateOcrEngine(string? atlasDirectory = null)
    {
        AtlasDirectory = atlasDirectory ?? GlyphAtlasLocator.Resolve();
        _atlas = GlyphAtlas.TryLoad(AtlasDirectory);
    }

    public string Name => "Template";

    public string AtlasDirectory { get; }

    public bool IsAvailable => _atlas is not null;

    /// <summary>模板库覆盖了哪些字符。</summary>
    public string Coverage => _atlas?.Coverage ?? "";

    public Task<CoordinateOcrResult> RecognizeAsync(Mat input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        ObjectDisposedException.ThrowIf(_disposed, this);

        return Task.Run(() => Recognize(input, cancellationToken), cancellationToken);
    }

    private CoordinateOcrResult Recognize(Mat input, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        if (_atlas is null)
        {
            return CoordinateOcrResult.Failed(
                $"TEMPLATE_ATLAS_MISSING: {AtlasDirectory}", ocrTime: stopwatch.Elapsed);
        }

        if (input.Empty())
        {
            return CoordinateOcrResult.Failed("EMPTY_IMAGE", ocrTime: stopwatch.Elapsed);
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var text = ExtractText(input, cancellationToken);
            stopwatch.Stop();

            return new CoordinateOcrResult
            {
                Success = !string.IsNullOrWhiteSpace(text),
                RawText = text,

                // 模板匹配没有概率输出。这里给一个保守的固定值，
                // 真正的可信度由多流水线交叉验证给出。
                Confidence = string.IsNullOrWhiteSpace(text) ? 0.0 : 0.75,
                OcrTime = stopwatch.Elapsed,
                Error = string.IsNullOrWhiteSpace(text) ? "NO_TEXT_RECOGNIZED" : null,
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            stopwatch.Stop();
            return CoordinateOcrResult.Failed($"TEMPLATE_ERROR: {ex.Message}", ocrTime: stopwatch.Elapsed);
        }
    }

    private string ExtractText(Mat input, CancellationToken cancellationToken)
    {
        var glyphs = GlyphSegmenter.Segment(input);
        if (glyphs.Count == 0)
        {
            return "";
        }

        var lines = GlyphSegmenter.GroupIntoLines(glyphs, input.Height);
        var builder = new StringBuilder();

        foreach (var line in lines)
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var glyph in line)
            {
                builder.Append(RecognizeGlyph(glyph.Bitmap));
            }

            builder.Append('\n');
        }

        return builder.ToString();
    }

    private string RecognizeGlyph(Mat glyph)
    {
        using var normalized = GlyphNormalizer.Normalize(glyph);

        var (label, score) = _atlas!.Match(normalized);
        return score >= MatchThreshold ? label : "?";
    }

    public void Dispose()
    {
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
