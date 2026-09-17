namespace MortarHUD.Core.Validation;

/// <summary>坐标校验阈值（TDD §16）。</summary>
public sealed class CoordinateValidationOptions
{
    /// <summary>坐标下限（含）。</summary>
    public double CoordinateMin { get; init; } = 0.0;

    /// <summary>坐标上限（含）。地图实际只用到约 0–130，默认给足余量。</summary>
    public double CoordinateMax { get; init; } = 200.0;

    /// <summary>OCR 置信度下限。</summary>
    public double MinimumConfidence { get; init; } = 0.60;

    /// <summary>是否强制要求两轴都识别成功。</summary>
    public bool RequireBothAxes { get; init; } = true;

    public static CoordinateValidationOptions Default { get; } = new();
}
