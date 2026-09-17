using MortarHUD.Core.Ballistics;
using MortarHUD.Core.Models;
using Xunit;

namespace MortarHUD.Core.Tests;

/// <summary>
/// 弹道解算测试（TDD §41.1）。
/// </summary>
/// <remarks>
/// 方位角是这里最容易写错的地方：<c>Atan2</c> 的参数顺序必须是
/// (东分量, 北分量)，和数学课本上的 (y, x) 相反。这组用例把八个方向全钉住，
/// 一旦有人「顺手改成 (dy, dx)」，测试会立刻炸。
/// </remarks>
public class MortarCalculatorTests
{
    private static readonly MortarCalculator Calculator = new(metersPerCoordinateUnit: 100.0);

    [Theory]
    [InlineData(0, 1, 0.0, "正北")]
    [InlineData(1, 0, 90.0, "正东")]
    [InlineData(0, -1, 180.0, "正南")]
    [InlineData(-1, 0, 270.0, "正西")]
    public void Solve_CardinalDirections_ReturnsExpectedBearing(
        double targetX, double targetY, double expectedBearing, string direction)
    {
        var solution = Calculator.Solve(new MapCoordinate(0, 0), new MapCoordinate(targetX, targetY));

        Assert.Equal(expectedBearing, solution.BearingDegrees, precision: 9);
        Assert.Equal(100.0, solution.RangeMeters, precision: 9);
        Assert.False(string.IsNullOrEmpty(direction));
    }

    [Theory]
    [InlineData(1, 1, 45.0)]
    [InlineData(1, -1, 135.0)]
    [InlineData(-1, -1, 225.0)]
    [InlineData(-1, 1, 315.0)]
    public void Solve_Diagonals_ReturnsExpectedBearing(double targetX, double targetY, double expectedBearing)
    {
        var solution = Calculator.Solve(new MapCoordinate(0, 0), new MapCoordinate(targetX, targetY));

        Assert.Equal(expectedBearing, solution.BearingDegrees, precision: 9);
    }

    [Fact]
    public void Solve_SamePosition_ReturnsZeroRangeAndBearing()
    {
        var solution = Calculator.Solve(new MapCoordinate(42.5, 71.25), new MapCoordinate(42.5, 71.25));

        Assert.Equal(0.0, solution.RangeMeters, precision: 9);
        Assert.Equal(0.0, solution.BearingDegrees, precision: 9);
        Assert.Equal(0.0, solution.DeltaX, precision: 9);
        Assert.Equal(0.0, solution.DeltaY, precision: 9);
    }

    /// <summary>
    /// TDD §9 的示例数，同时覆盖「坐标缩放/平移不影响结果」这一条验收条件：
    /// 把两个点整体平移同一个量，解算结果必须一字不差。
    /// </summary>
    [Fact]
    public void Solve_TddWorkedExample_MatchesDocumentedValues()
    {
        var gun = new MapCoordinate(98.09, 109.78);
        var target = new MapCoordinate(99.58, 110.07);

        var solution = Calculator.Solve(gun, target);

        Assert.Equal(1.49, solution.DeltaX, precision: 6);
        Assert.Equal(0.29, solution.DeltaY, precision: 6);
        Assert.Equal(151.8, solution.RangeMeters, precision: 1);
        Assert.Equal(79.0, solution.BearingDegrees, precision: 1);
    }

    [Theory]
    [InlineData(1000.0, -2000.0)]
    [InlineData(-37.5, 512.25)]
    public void Solve_TranslationInvariant(double offsetX, double offsetY)
    {
        var reference = Calculator.Solve(new MapCoordinate(98.09, 109.78), new MapCoordinate(99.58, 110.07));

        var shifted = Calculator.Solve(
            new MapCoordinate(98.09 + offsetX, 109.78 + offsetY),
            new MapCoordinate(99.58 + offsetX, 110.07 + offsetY));

        Assert.Equal(reference.RangeMeters, shifted.RangeMeters, precision: 9);
        Assert.Equal(reference.BearingDegrees, shifted.BearingDegrees, precision: 9);
    }

    [Fact]
    public void Solve_BearingAlwaysInHalfOpenRange()
    {
        var random = new Random(20260916);

        for (var i = 0; i < 2000; i++)
        {
            var target = new MapCoordinate(
                random.NextDouble() * 200 - 100,
                random.NextDouble() * 200 - 100);

            var solution = Calculator.Solve(new MapCoordinate(0, 0), target);

            Assert.InRange(solution.BearingDegrees, 0.0, 359.999999999);
            Assert.True(solution.RangeMeters >= 0);
        }
    }

    [Fact]
    public void Solve_RespectsMetersPerCoordinateUnit()
    {
        var calculator = new MortarCalculator(metersPerCoordinateUnit: 250.0);

        var solution = calculator.Solve(new MapCoordinate(0, 0), new MapCoordinate(3, 4));

        // 3-4-5 直角三角形，坐标距离 5，换算后 1250m。
        Assert.Equal(1250.0, solution.RangeMeters, precision: 9);
    }

    /// <summary>方向轴可以翻转，用于适配沿用不同坐标系约定的游戏版本。</summary>
    [Fact]
    public void Solve_FlippedAxes_ProduceMirroredResult()
    {
        var calculator = new MortarCalculator(100.0, xPositiveIsEast: false, yPositiveIsNorth: true);

        var solution = calculator.Solve(new MapCoordinate(0, 0), new MapCoordinate(1, 0));

        // +X 指向西时，向 +X 移动 1 单位即向西，方位角 270°。
        Assert.Equal(270.0, solution.BearingDegrees, precision: 9);

        // 但 Δ 仍然按原始坐标给出，便于显示。
        Assert.Equal(1.0, solution.DeltaX, precision: 9);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    public void Constructor_InvalidScale_Throws(double scale)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MortarCalculator(scale));
    }
}
