namespace MortarHUD.Core.Parsing;

/// <summary>解析一条 OCR 文本的结果。</summary>
public sealed record CoordinateParseResult
{
    public bool Success { get; init; }

    public double? X { get; init; }
    public double? Y { get; init; }

    /// <summary>经过字符修正后的文本，便于 Debug 面板对比原始 OCR 输出。</summary>
    public string RepairedText { get; init; } = "";

    public string? Error { get; init; }

    public static CoordinateParseResult Ok(double x, double y, string repairedText) => new()
    {
        Success = true,
        X = x,
        Y = y,
        RepairedText = repairedText,
    };

    public static CoordinateParseResult Fail(string error, string repairedText = "") => new()
    {
        Success = false,
        Error = error,
        RepairedText = repairedText,
    };
}
