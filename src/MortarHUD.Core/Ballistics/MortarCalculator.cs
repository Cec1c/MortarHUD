using MortarHUD.Core.Models;
using MortarHUD.Localization;

namespace MortarHUD.Core.Ballistics;

/// <summary>
/// 平面解算：只做 Δ、距离与方位角，不做弹道模拟、不做风偏修正（见 TDD §4 非目标）。
/// </summary>
/// <remarks>
/// 坐标约定（TDD §2）：+X = 东，+Y = 北；方位角 北=0°，东=90°，南=180°，西=270°。
/// 因此方位角用 <c>Atan2(dx, dy)</c>——参数顺序是 (东分量, 北分量)，
/// 不是数学上常见的 (y, x)，两者不可互换。
/// </remarks>
public sealed class MortarCalculator : IMortarCalculator
{
    public const double DefaultMetersPerCoordinateUnit = 100.0;

    private readonly bool _xPositiveIsEast;
    private readonly bool _yPositiveIsNorth;

    /// <param name="metersPerCoordinateUnit">每个坐标单位的米数，必须为正。</param>
    /// <param name="xPositiveIsEast">+X 是否指向东；设为 false 表示 +X 指向西。</param>
    /// <param name="yPositiveIsNorth">+Y 是否指向北；设为 false 表示 +Y 指向南。</param>
    public MortarCalculator(
        double metersPerCoordinateUnit = DefaultMetersPerCoordinateUnit,
        bool xPositiveIsEast = true,
        bool yPositiveIsNorth = true)
    {
        if (!double.IsFinite(metersPerCoordinateUnit) || metersPerCoordinateUnit <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(metersPerCoordinateUnit),
                metersPerCoordinateUnit,
                Loc.T("MetersPerCoordinateUnitMustBeAPositiveFiniteNumb"));
        }

        MetersPerCoordinateUnit = metersPerCoordinateUnit;
        _xPositiveIsEast = xPositiveIsEast;
        _yPositiveIsNorth = yPositiveIsNorth;
    }

    public double MetersPerCoordinateUnit { get; }

    public MortarSolution Solve(MapCoordinate gun, MapCoordinate target)
    {
        var rawDx = target.X - gun.X;
        var rawDy = target.Y - gun.Y;

        // 把游戏坐标系折算成「东 / 北」分量，后续算法只认这两个方向。
        var east = _xPositiveIsEast ? rawDx : -rawDx;
        var north = _yPositiveIsNorth ? rawDy : -rawDy;

        var coordinateDistance = Math.Sqrt(east * east + north * north);
        var rangeMeters = coordinateDistance * MetersPerCoordinateUnit;

        double bearing;
        if (east == 0.0 && north == 0.0)
        {
            // 同点：Atan2(0,0) 虽返回 0，但显式处理可以避免 -0.0 之类的符号噪声。
            bearing = 0.0;
        }
        else
        {
            bearing = Math.Atan2(east, north) * 180.0 / Math.PI;

            // 归一化到 [0, 360)
            if (bearing < 0.0)
            {
                bearing += 360.0;
            }

            // 浮点误差可能让 -1e-14 加完 360 之后正好等于 360。压回 0。
            if (bearing >= 360.0)
            {
                bearing -= 360.0;
            }
        }

        return new MortarSolution(rawDx, rawDy, rangeMeters, bearing);
    }
}
