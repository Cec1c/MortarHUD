namespace MortarHUD.Core.Models;

/// <summary>
/// 游戏地图上的绝对坐标（游戏内单位）。
/// </summary>
/// <remarks>
/// 默认坐标系：+X = 东（East），+Y = 北（North）。
/// 1 个坐标单位 ≈ 100 米，但换算比例不在这里硬编码，
/// 由 <see cref="Ballistics.MortarCalculator"/> 通过配置注入。
/// </remarks>
public readonly record struct MapCoordinate(double X, double Y)
{
    /// <summary>两个分量都是有限数（非 NaN / 非无穷）。</summary>
    public bool IsFinite => double.IsFinite(X) && double.IsFinite(Y);

    public override string ToString() => $"{X:0.00} / {Y:0.00}";
}
