using MortarHUD.Core.Configuration;
using MortarHUD.Core.Models;
using MortarHUD.Core.Session;
using MortarHUD.Core.Themes;
using Xunit;

namespace MortarHUD.Core.Tests;

/// <summary>Debug 面板排版测试（TDD §34）。</summary>
public class DebugInfoFormatterTests
{
    private static DebugSnapshot CreateSnapshot(bool success = true) => new()
    {
        CursorX = 1534,
        CursorY = 682,
        Roi = new System.Drawing.Rectangle(1544, 602, 240, 140),
        Success = success,
        Coordinate = success ? new MapCoordinate(107.66, 114.54) : null,
        Confidence = 0.94,
        RawText = "y114.54 x107.66",
        Error = success ? null : "X_NOT_FOUND",
        OcrMilliseconds = 18.4,
        TotalMilliseconds = 24.1,
        PipelineSummary = ["Tesseract/A OK", "Tesseract/C FAIL X_NOT_FOUND"],
    };

    private static DebugSettings AllEnabled() => new()
    {
        Enabled = true,
        ShowRoiRectangle = true,
        ShowCursorAnchor = true,
        ShowRawOcrText = true,
        ShowParsedCoordinates = true,
        ShowConfidence = true,
        ShowTiming = true,
    };

    [Fact]
    public void BuildLines_Disabled_ReturnsNothing()
    {
        var lines = DebugInfoFormatter.BuildLines(CreateSnapshot(), new DebugSettings { Enabled = false });

        Assert.Empty(lines);
    }

    [Fact]
    public void IsVisible_EnabledButNothingSelected_ReturnsFalse()
    {
        // 打开了 Debug 却一个显示项都没勾：面板里只剩一块底板。
        // 注意不能用 BuildLines 是否为空来判断——流水线摘要是无条件追加的。
        var hud = new HudSettings { Visible = true };
        var debug = new DebugSettings { Enabled = true };
        Assert.NotEmpty(DebugInfoFormatter.BuildLines(CreateSnapshot(), debug));

        Assert.False(DebugInfoFormatter.IsVisible(hud, debug));
    }

    [Fact]
    public void IsVisible_AnySectionSelected_ReturnsTrue()
    {
        var hud = new HudSettings { Visible = true };
        var debug = new DebugSettings { Enabled = true, ShowConfidence = true };

        Assert.True(DebugInfoFormatter.IsVisible(hud, debug));
    }

    [Fact]
    public void IsVisible_HudHidden_ReturnsFalse()
    {
        // 主 HUD 都关了，调试面板不该自己留着。
        var hud = new HudSettings { Visible = false };

        Assert.False(DebugInfoFormatter.IsVisible(hud, AllEnabled()));
    }

    [Fact]
    public void IsVisible_Disabled_ReturnsFalse()
    {
        var hud = new HudSettings { Visible = true };
        var debug = AllEnabled();
        debug.Enabled = false;

        Assert.False(DebugInfoFormatter.IsVisible(hud, debug));
    }

    [Fact]
    public void BuildLines_AllEnabled_ProducesDocumentedSections()
    {
        var lines = DebugInfoFormatter.BuildLines(CreateSnapshot(), AllEnabled());
        var text = string.Join("\n", lines.Select(l => l.ToText()));

        Assert.Contains("CURSOR 1534,682", text, StringComparison.Ordinal);
        Assert.Contains("ROI", text, StringComparison.Ordinal);
        Assert.Contains("X 1544", text, StringComparison.Ordinal);
        Assert.Contains("Y 602", text, StringComparison.Ordinal);
        Assert.Contains("W 240", text, StringComparison.Ordinal);
        Assert.Contains("H 140", text, StringComparison.Ordinal);
        Assert.Contains("CONF 0.94", text, StringComparison.Ordinal);
        Assert.Contains("18.4ms", text, StringComparison.Ordinal);
        Assert.Contains("y114.54 x107.66", text, StringComparison.Ordinal);
        Assert.Contains("107.66", text, StringComparison.Ordinal);
        Assert.Contains("114.54", text, StringComparison.Ordinal);
    }

    /// <summary>ROI 的四个数字必须落在正确的行上——打反了会把排查带偏很久。</summary>
    /// <remarks>
    /// 比较前先 Trim：只有标签没有数值的行会留一个用于对齐的空格
    /// （Detailed 布局靠它把数值推到同一列），那是刻意的，不属于内容。
    /// </remarks>
    [Fact]
    public void BuildLines_RoiDimensionsAreNotSwapped()
    {
        var lines = DebugInfoFormatter.BuildLines(CreateSnapshot(), AllEnabled());
        var texts = lines.Select(l => l.ToText().Trim()).ToList();

        var roiIndex = texts.IndexOf("ROI");

        Assert.True(roiIndex >= 0, $"没找到 ROI 标题行，实际内容：{string.Join(" | ", texts)}");
        Assert.Equal("X 1544", texts[roiIndex + 1]);
        Assert.Equal("Y 602", texts[roiIndex + 2]);
        Assert.Equal("W 240", texts[roiIndex + 3]);
        Assert.Equal("H 140", texts[roiIndex + 4]);
    }

    [Fact]
    public void BuildLines_Failure_ShowsErrorInsteadOfCoordinate()
    {
        var lines = DebugInfoFormatter.BuildLines(CreateSnapshot(success: false), AllEnabled());
        var text = string.Join("\n", lines.Select(l => l.ToText()));

        Assert.Contains("FAIL", text, StringComparison.Ordinal);
        Assert.Contains("X_NOT_FOUND", text, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildLines_RespectsIndividualToggles()
    {
        var settings = new DebugSettings { Enabled = true, ShowCursorAnchor = true };

        var lines = DebugInfoFormatter.BuildLines(CreateSnapshot(), settings);
        var text = string.Join("\n", lines.Select(l => l.ToText()));

        Assert.Contains("CURSOR", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ROI", text, StringComparison.Ordinal);
        Assert.DoesNotContain("CONF", text, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildLines_EmptyRawText_ShowsPlaceholder()
    {
        var settings = AllEnabled();
        var snapshot = CreateSnapshot() with { RawText = "" };

        var lines = DebugInfoFormatter.BuildLines(snapshot, settings);
        var text = string.Join("\n", lines.Select(l => l.ToText()));

        Assert.Contains("（空）", text, StringComparison.Ordinal);
    }

    /// <summary>Debug 面板必须比主 HUD 更小更淡，否则会挡住游戏视野。</summary>
    [Fact]
    public void BuildDebugTheme_IsSmallerAndHasPanelBackground()
    {
        var source = HudThemeLibrary.CreateDefaultGreen();

        var theme = DebugInfoFormatter.BuildDebugTheme(source);

        Assert.True(theme.FontSize < source.FontSize);
        Assert.Equal(HudBackgroundMode.TransparentPanel, theme.Background);
        Assert.NotEqual(source.Background, theme.Background);

        // 不能改到原主题对象。
        Assert.Equal(HudBackgroundMode.None, source.Background);
    }
}
