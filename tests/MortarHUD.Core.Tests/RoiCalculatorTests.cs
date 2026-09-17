using MortarHUD.Capture.ScreenCapture;
using MortarHUD.Core.Configuration;
using Xunit;
using Rectangle = System.Drawing.Rectangle;

namespace MortarHUD.Core.Tests;

/// <summary>ROI 计算测试（TDD §10.2 / §12）。</summary>
public class RoiCalculatorTests
{
    private static readonly Rectangle FullHd = new(0, 0, 1920, 1080);

    [Fact]
    public void ComputeRaw_DefaultSettings_MatchesMeasuredReference()
    {
        var roi = new RoiSettings { AutoScale = false };

        var rect = RoiCalculator.ComputeRaw(1000, 500, roi, 1080);

        Assert.Equal(1000 + RoiSettings.ReferenceOffsetX, rect.X);
        Assert.Equal(500 + RoiSettings.ReferenceOffsetY, rect.Y);
        Assert.Equal(RoiSettings.ReferenceWidth, rect.Width);
        Assert.Equal(RoiSettings.ReferenceHeight, rect.Height);
    }

    /// <summary>
    /// 默认 ROI 必须把实机截图里测出来的文字块完整包住。
    /// </summary>
    /// <remarks>
    /// 实测（1920x1080）：y 行左上角约在光标 +(21, -57)，整块读数约 70x67。
    /// 这条测试把「默认值能框住文字」这个结论固化下来——
    /// 以后谁改了默认值，这里会立刻发现。
    /// </remarks>
    [Fact]
    public void ComputeRaw_DefaultSettings_CoversMeasuredLabelBlock()
    {
        var roi = new RoiSettings { AutoScale = false };
        var rect = RoiCalculator.ComputeRaw(1000, 500, roi, 1080);

        var labelLeft = 1000 + 21;
        var labelTop = 500 - 57;
        var labelRight = labelLeft + 90;   // 允许坐标到 3 位整数时的最大宽度
        var labelBottom = labelTop + 70;

        Assert.True(rect.Left <= labelLeft, $"ROI 左边 {rect.Left} 没盖住文字左边 {labelLeft}");
        Assert.True(rect.Top <= labelTop, $"ROI 上边 {rect.Top} 没盖住文字上边 {labelTop}");
        Assert.True(rect.Right >= labelRight, $"ROI 右边 {rect.Right} 没盖住文字右边 {labelRight}");
        Assert.True(rect.Bottom >= labelBottom, $"ROI 下边 {rect.Bottom} 没盖住文字下边 {labelBottom}");
    }

    [Theory]
    [InlineData(1080, 1.0)]
    [InlineData(1440, 1440.0 / 1080.0)]
    [InlineData(2160, 2.0)]
    public void ResolveScale_AutoScale_TracksScreenHeight(int screenHeight, double expected)
    {
        var roi = new RoiSettings { AutoScale = true };

        Assert.Equal(expected, RoiCalculator.ResolveScale(roi, screenHeight), precision: 6);
    }

    [Fact]
    public void ResolveScale_AutoScaleDisabled_UsesConfiguredValue()
    {
        var roi = new RoiSettings { AutoScale = false, Scale = 1.5 };

        Assert.Equal(1.5, RoiCalculator.ResolveScale(roi, 2160), precision: 6);
    }

    [Fact]
    public void ComputeRaw_WithAutoScale_ScalesOffsetsToo()
    {
        var roi = new RoiSettings { AutoScale = true };

        var rect = RoiCalculator.ComputeRaw(1000, 500, roi, 2160);

        Assert.Equal(1000 + RoiSettings.ReferenceOffsetX * 2, rect.X);
        Assert.Equal(500 + RoiSettings.ReferenceOffsetY * 2, rect.Y);
        Assert.Equal(RoiSettings.ReferenceWidth * 2, rect.Width);
    }

    /// <summary>光标贴着屏幕边角时，ROI 必须被裁进屏幕范围，而不是让 BitBlt 去读屏外。</summary>
    [Fact]
    public void ClampTo_NearScreenEdge_TrimsRectangle()
    {
        var requested = new Rectangle(1900, 1060, 150, 140);

        var clamped = RoiCalculator.ClampTo(requested, FullHd);

        Assert.Equal(1900, clamped.Left);
        Assert.Equal(1060, clamped.Top);
        Assert.Equal(20, clamped.Width);
        Assert.Equal(20, clamped.Height);
    }

    [Fact]
    public void ClampTo_FullyOutside_ReturnsPlaceholderRatherThanEmpty()
    {
        var clamped = RoiCalculator.ClampTo(new Rectangle(-500, -500, 100, 100), FullHd);

        Assert.True(clamped.Width > 0);
        Assert.True(clamped.Height > 0);
    }

    [Fact]
    public void ClampTo_SecondMonitorNegativeOrigin_KeepsRectangle()
    {
        // 主屏左边再挂一块显示器时，虚拟桌面原点会变成负数。
        var virtualScreen = new Rectangle(-1920, 0, 3840, 1080);
        var requested = new Rectangle(-1800, 200, 150, 140);

        var clamped = RoiCalculator.ClampTo(requested, virtualScreen);

        Assert.Equal(requested, clamped);
    }

    [Fact]
    public void Compute_CombinesClampingAndScaling()
    {
        var roi = new RoiSettings { AutoScale = false };

        var rect = RoiCalculator.Compute(5, 5, roi, 1080, FullHd);

        Assert.True(rect.Left >= 0);
        Assert.True(rect.Top >= 0);
        Assert.True(rect.Width > 0 && rect.Height > 0);
    }
}
