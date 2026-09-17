namespace MortarHUD.Core.Models;

/// <summary>
/// 一次 OCR 识别的原始产物。注意：它<strong>尚未</strong>经过解析与校验，
/// 不能直接当作可信坐标使用。
/// </summary>
public sealed record CoordinateOcrResult
{
    public bool Success { get; init; }

    public double? X { get; init; }
    public double? Y { get; init; }

    public string RawText { get; init; } = "";
    public double Confidence { get; init; }

    public string? Error { get; init; }

    public TimeSpan CaptureTime { get; init; }
    public TimeSpan PreprocessTime { get; init; }
    public TimeSpan OcrTime { get; init; }

    /// <summary>端到端耗时。</summary>
    public TimeSpan TotalTime => CaptureTime + PreprocessTime + OcrTime;

    /// <summary>识别失败的结果，保留原始文本与耗时以便 Debug 面板展示。</summary>
    public static CoordinateOcrResult Failed(
        string error,
        string rawText = "",
        TimeSpan captureTime = default,
        TimeSpan preprocessTime = default,
        TimeSpan ocrTime = default) => new()
        {
            Success = false,
            Error = error,
            RawText = rawText,
            CaptureTime = captureTime,
            PreprocessTime = preprocessTime,
            OcrTime = ocrTime,
        };
}
