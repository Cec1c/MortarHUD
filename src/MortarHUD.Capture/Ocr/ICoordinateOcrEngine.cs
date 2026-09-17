using MortarHUD.Core.Models;
using OpenCvSharp;

namespace MortarHUD.Capture.Ocr;

/// <summary>
/// 本地 OCR 引擎（TDD §13）。
/// </summary>
/// <remarks>
/// <para>契约：</para>
/// <list type="bullet">
///   <item>完全本地，不联网、不需要 Python 运行时。</item>
///   <item>输入是<strong>预处理之后</strong>的二值图（见 <see cref="ImageProcessing.IImagePreprocessor"/>）。</item>
///   <item>
///     实现只负责「把图上的字读出来」：填 <see cref="CoordinateOcrResult.RawText"/> 与
///     <see cref="CoordinateOcrResult.Confidence"/>，<c>X</c>/<c>Y</c> 一律留 null。
///     数值解析由 <see cref="MortarHUD.Core.Parsing.ICoordinateTextParser"/> 负责——
///     TDD §15 明确要求 OCR 不参与最终可信判断。
///   </item>
/// </list>
/// <para>实现必须可替换，以便 TDD §17 的 Benchmark 横向比较。</para>
/// </remarks>
public interface ICoordinateOcrEngine : IDisposable
{
    /// <summary>引擎名字，用于设置页与 Benchmark 报告。</summary>
    string Name { get; }

    Task<CoordinateOcrResult> RecognizeAsync(Mat input, CancellationToken cancellationToken);
}

/// <summary>OCR 引擎不可用（例如缺少语言包）。</summary>
public sealed class OcrEngineUnavailableException : Exception
{
    public OcrEngineUnavailableException(string message) : base(message)
    {
    }

    public OcrEngineUnavailableException(string message, Exception inner) : base(message, inner)
    {
    }
}
