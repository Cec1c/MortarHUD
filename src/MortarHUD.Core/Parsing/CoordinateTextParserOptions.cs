namespace MortarHUD.Core.Parsing;

/// <summary>解析器行为配置。默认值刻意偏「严格」。</summary>
public sealed class CoordinateTextParserOptions
{
    /// <summary>
    /// 必须出现小数点，否则判定失败。
    /// 开启后可挡住「OCR 丢掉了小数部分」这类静默损坏，例如 x98.09 被识别成 x98。
    /// </summary>
    public bool RequireDecimalPoint { get; init; } = true;

    /// <summary>
    /// 允许的小数位数。游戏固定输出 2 位（98.09 / 109.78），
    /// 因此默认只接受 2 位——这样 x98.9 这种「丢了一位」的结果会被直接拒绝，
    /// 而不是被当成 98.9 用下去（那是 81 米的误差）。
    /// </summary>
    public int[] AllowedFractionDigits { get; init; } = [2];

    /// <summary>整数部分允许的最大位数。</summary>
    public int MaxIntegerDigits { get; init; } = 3;

    /// <summary>是否允许负坐标（TDD §15 的正则包含 -?）。</summary>
    public bool AllowNegative { get; init; } = true;

    /// <summary>
    /// 是否启用数字上下文内的 OCR 字符修正（O/o → 0，I/l/| → 1）。
    /// 修正只发生在 x/y 之后的数字串里，不会碰其他位置。
    /// </summary>
    public bool EnableDigitRepair { get; init; } = true;

    /// <summary>
    /// 同一根轴上出现多个不同候选值时是否判定失败。
    /// 默认开启：宁可报错，也不要静默挑一个可能是噪声的值。
    /// </summary>
    public bool RejectAmbiguousAxis { get; init; } = true;

    public static CoordinateTextParserOptions Default { get; } = new();
}
