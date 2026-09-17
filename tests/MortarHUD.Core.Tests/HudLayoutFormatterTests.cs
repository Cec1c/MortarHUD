using MortarHUD.Core.Ballistics;
using MortarHUD.Core.Configuration;
using MortarHUD.Core.Models;
using MortarHUD.Core.Session;
using MortarHUD.Core.Themes;
using Xunit;

namespace MortarHUD.Core.Tests;

/// <summary>HUD 排版测试（TDD §20 / §25）。</summary>
public class HudLayoutFormatterTests
{
    private static MortarSession CreateSolvedSession()
    {
        var session = new MortarSession(new MortarCalculator(100.0));
        session.LockGun(new MapCoordinate(98.09, 109.78));
        session.LockTarget(new MapCoordinate(99.58, 110.07));
        return session;
    }

    /// <summary>TDD §20 的默认显示：AZ 079.0°，RNG 152m。</summary>
    [Fact]
    public void Compact_DefaultTheme_ProducesDocumentedText()
    {
        var session = CreateSolvedSession();
        var hud = new HudSettings { CurrentTheme = HudThemeLibrary.CreateDefaultGreen() };

        var lines = HudLayoutFormatter.BuildLines(session, hud);

        Assert.Equal("AZ  079.0°", lines[0].ToText());
        Assert.Equal("RNG 152m", lines[1].ToText());
    }

    [Theory]
    [InlineData(79.0, 1, "079.0°")]
    [InlineData(79.0, 0, "079°")]
    [InlineData(79.0, 2, "079.00°")]
    [InlineData(9.5, 1, "009.5°")]
    [InlineData(359.94, 1, "359.9°")]
    [InlineData(7.0, 1, "007.0°")]
    public void FormatBearing_PadsIntegerPart(double bearing, int decimals, string expected)
    {
        Assert.Equal(expected, HudLayoutFormatter.FormatBearing(bearing, decimals));
    }

    /// <summary>方位角恒在 [0,360)，360.0 不该被显示出来。</summary>
    [Fact]
    public void FormatBearing_NormalizesFullCircle()
    {
        Assert.Equal("000.0°", HudLayoutFormatter.FormatBearing(360.0, 1));
    }

    [Theory]
    [InlineData(151.8, 0, "152m")]
    [InlineData(151.8, 1, "151.8m")]
    [InlineData(0.0, 0, "0m")]
    public void FormatRange_RespectsDecimals(double meters, int decimals, string expected)
    {
        Assert.Equal(expected, HudLayoutFormatter.FormatRange(meters, decimals));
    }

    [Fact]
    public void Minimal_HidesLabels()
    {
        var session = CreateSolvedSession();
        var hud = new HudSettings { CurrentTheme = HudThemeLibrary.CreateDefaultGreen() };
        hud.CurrentTheme.Layout = HudLayout.Minimal;

        var lines = HudLayoutFormatter.BuildLines(session, hud);

        Assert.Equal("079.0°", lines[0].ToText());
        Assert.Equal("152m", lines[1].ToText());
    }

    [Fact]
    public void Horizontal_PutsBothOnOneLine()
    {
        var session = CreateSolvedSession();
        var hud = new HudSettings { CurrentTheme = HudThemeLibrary.CreateDefaultGreen() };
        hud.CurrentTheme.Layout = HudLayout.Horizontal;

        var lines = HudLayoutFormatter.BuildLines(session, hud);

        Assert.Single(lines);
        Assert.Contains("AZ", lines[0].ToText(), StringComparison.Ordinal);
        Assert.Contains("RNG", lines[0].ToText(), StringComparison.Ordinal);
    }

    [Fact]
    public void Detailed_ShowsGunAndTargetWhenEnabled()
    {
        var session = CreateSolvedSession();
        var hud = new HudSettings
        {
            CurrentTheme = HudThemeLibrary.CreateDefaultGreen(),
            ShowGun = true,
            ShowTarget = true,
        };
        hud.CurrentTheme.Layout = HudLayout.Detailed;

        var lines = HudLayoutFormatter.BuildLines(session, hud);

        Assert.Equal(4, lines.Count);
        Assert.Contains("98.09", lines[0].ToText(), StringComparison.Ordinal);
        Assert.Contains("99.58", lines[1].ToText(), StringComparison.Ordinal);
    }

    /// <summary>还没解算完就不该显示 000.0° / 0m——那会被当成真实结果。</summary>
    [Fact]
    public void WithoutTarget_ShowsPlaceholdersNotZeros()
    {
        var session = new MortarSession(new MortarCalculator(100.0));
        session.LockGun(new MapCoordinate(98.09, 109.78));

        var hud = new HudSettings { CurrentTheme = HudThemeLibrary.CreateDefaultGreen() };
        var lines = HudLayoutFormatter.BuildLines(session, hud);

        Assert.Equal("AZ  --", lines[0].ToText());
        Assert.Equal("RNG --", lines[1].ToText());
    }

    [Fact]
    public void ShowFlags_RemoveLines()
    {
        var session = CreateSolvedSession();
        var hud = new HudSettings
        {
            CurrentTheme = HudThemeLibrary.CreateDefaultGreen(),
            ShowAz = false,
        };

        var lines = HudLayoutFormatter.BuildLines(session, hud);

        Assert.Single(lines);
        Assert.StartsWith("RNG", lines[0].ToText(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(MortarStatusKind.GunLocked, "炮位已锁定")]
    [InlineData(MortarStatusKind.TargetLocked, "目标已锁定")]
    [InlineData(MortarStatusKind.OcrFailed, "识别失败")]
    [InlineData(MortarStatusKind.NoGunPosition, "未记录炮位")]
    [InlineData(MortarStatusKind.InvalidCoordinate, "坐标超出范围")]
    [InlineData(MortarStatusKind.CaptureFailed, "截图失败")]
    [InlineData(MortarStatusKind.CaptureCancelled, "光标移动，已取消")]
    [InlineData(MortarStatusKind.AutoCalibrateSkipped, "地图没打开")]
    [InlineData(MortarStatusKind.None, "")]
    public void DescribeStatus_IsLocalized(MortarStatusKind status, string expected)
    {
        Assert.Equal(expected, HudLayoutFormatter.DescribeStatus(status));
    }

    /// <summary>
    /// TDD §16：OCR 失败时必须让用户看到「目标没变」，
    /// 否则用户会以为新目标已经锁上了。
    /// </summary>
    [Theory]
    [InlineData(MortarStatusKind.OcrFailed)]
    [InlineData(MortarStatusKind.InvalidCoordinate)]
    [InlineData(MortarStatusKind.CaptureFailed)]
    [InlineData(MortarStatusKind.CaptureCancelled)]
    public void DescribeStatusDetail_FailuresSayTargetUnchanged(MortarStatusKind status)
    {
        Assert.Equal("目标未改变", HudLayoutFormatter.DescribeStatusDetail(status));
    }

    [Fact]
    public void DescribeStatusDetail_SuccessHasNoDetail()
    {
        Assert.Null(HudLayoutFormatter.DescribeStatusDetail(MortarStatusKind.TargetLocked));
    }
}
