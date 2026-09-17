using MortarHUD.Core.Models;

namespace MortarHUD.Core.Ballistics;

/// <summary>炮位 → 目标的解算器。</summary>
public interface IMortarCalculator
{
    /// <summary>每 1 个坐标单位对应的米数。</summary>
    double MetersPerCoordinateUnit { get; }

    MortarSolution Solve(MapCoordinate gun, MapCoordinate target);
}
