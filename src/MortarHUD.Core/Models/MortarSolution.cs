namespace MortarHUD.Core.Models;

/// <summary>
/// 炮位 → 目标的解算结果。
/// </summary>
/// <param name="DeltaX">目标与炮位的 X 差值（坐标单位）。</param>
/// <param name="DeltaY">目标与炮位的 Y 差值（坐标单位）。</param>
/// <param name="RangeMeters">直线距离（米）。</param>
/// <param name="BearingDegrees">方位角，范围 [0, 360)。0=北，90=东，180=南，270=西。</param>
public readonly record struct MortarSolution(
    double DeltaX,
    double DeltaY,
    double RangeMeters,
    double BearingDegrees
)
{
    /// <summary>尚未解算时的占位值。</summary>
    public static MortarSolution Empty { get; } = new(0, 0, 0, 0);
}
