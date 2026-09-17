using MortarHUD.Core.Models;

namespace MortarHUD.Capture.Ocr;

/// <summary>
/// 一次「引擎 × 预处理流水线」的完整尝试记录。
/// Debug 面板与 Benchmark 都靠它还原「当时到底发生了什么」。
/// </summary>
public sealed record RecognitionAttempt
{
    public required string Engine { get; init; }

    public required string Pipeline { get; init; }

    /// <summary>预处理图的尺寸，Debug 预览用。</summary>
    public int ProcessedWidth { get; init; }

    public int ProcessedHeight { get; init; }

    public string RawText { get; init; } = "";

    public string RepairedText { get; init; } = "";

    public double Confidence { get; init; }

    /// <summary>成功时是解析 + 校验后的坐标。</summary>
    public MapCoordinate? Coordinate { get; init; }

    public bool Success => Coordinate is not null;

    /// <summary>失败原因；成功时为 null。</summary>
    public string? Error { get; init; }

    public TimeSpan PreprocessTime { get; init; }

    public TimeSpan OcrTime { get; init; }

    public TimeSpan TotalTime => PreprocessTime + OcrTime;

    public override string ToString()
        => $"{Engine}/{Pipeline} "
           + (Success
               ? $"OK {Coordinate!.Value.X:0.00}/{Coordinate.Value.Y:0.00} conf={Confidence:0.00}"
               : $"FAIL {Error}");
}

/// <summary>
/// 识别过程中的图像回调（TDD §33 的 Debug 转储靠它拿到像素）。
/// </summary>
/// <remarks>
/// 回调发生在 <see cref="OpenCvSharp.Mat"/> 被释放<em>之前</em>，
/// 实现方要么当场把图写盘，要么自己 Clone 一份带走——不能只存引用。
/// </remarks>
public interface IRecognitionObserver
{
    void OnRawRoi(OpenCvSharp.Mat rawRoi);

    void OnProcessed(string engine, string pipeline, OpenCvSharp.Mat processed);
}

/// <summary>一次坐标识别的最终结论。</summary>
public sealed record RecognitionOutcome
{
    public bool Success { get; init; }

    public MapCoordinate? Coordinate { get; init; }

    public string? Error { get; init; }

    /// <summary>最终置信度，喂给 <c>MinimumConfidence</c> 判断。</summary>
    public double Confidence { get; init; }

    /// <summary>胜出的原始 OCR 文本。</summary>
    public string RawText { get; init; } = "";

    /// <summary>本次尝试过的全部组合，供 Debug 面板展示。</summary>
    public IReadOnlyList<RecognitionAttempt> Attempts { get; init; } = [];

    public TimeSpan TotalTime { get; init; }

    public static RecognitionOutcome Failed(string error, IReadOnlyList<RecognitionAttempt> attempts, TimeSpan total)
        => new() { Success = false, Error = error, Attempts = attempts, TotalTime = total };
}
