using System.Globalization;
using MortarHUD.Localization;
using MortarHUD.Core.Configuration;
using MortarHUD.Core.Models;
using MortarHUD.Core.Themes;

namespace MortarHUD.Core.Session;

/// <summary>一次采集的 Debug 摘要，喂给 <see cref="DebugInfoFormatter"/> 排版。</summary>
public sealed record DebugSnapshot
{
    public int CursorX { get; init; }
    public int CursorY { get; init; }

    public System.Drawing.Rectangle Roi { get; init; }

    public bool Success { get; init; }
    public MapCoordinate? Coordinate { get; init; }
    public double Confidence { get; init; }
    public string RawText { get; init; } = "";
    public string? Error { get; init; }

    public double OcrMilliseconds { get; init; }
    public double TotalMilliseconds { get; init; }

    public IReadOnlyList<string> PipelineSummary { get; init; } = [];
}

/// <summary>
/// 把 Debug 快照排成 HUD 行（TDD §34）。
/// </summary>
/// <remarks>
/// 排版放在 Core 而不是窗口里，是为了能用单元测试固定住输出格式——
/// 调试信息一旦显示错（比如把 ROI 的 X/Y 打反），
/// 排查时会被带偏很久，而那种错误肉眼极难发现。
/// </remarks>
public static class DebugInfoFormatter
{
    /// <summary>
    /// 调试面板要不要显示。
    /// </summary>
    /// <remarks>
    /// 只看 <see cref="DebugSettings.Enabled"/> 是不够的：用户可能打开了 Debug 却
    /// 一个显示项都没勾，那样面板里只剩一块半透明底板——悬在屏幕上的灰色矩形。
    /// <para>
    /// 这里判的是「有没有勾选任何一项」，而**不是** <see cref="BuildLines"/> 是否为空：
    /// 流水线摘要是无条件追加的，只要 Enabled 为真就至少有一行，拿行数判断会永远为真。
    /// </para>
    /// 逻辑放在 Core 而不是窗口里，是为了能用单元测试钉住。
    /// </remarks>
    public static bool IsVisible(HudSettings hud, DebugSettings debug)
    {
        ArgumentNullException.ThrowIfNull(hud);
        ArgumentNullException.ThrowIfNull(debug);

        var anySectionSelected = debug.ShowCursorAnchor
            || debug.ShowRoiRectangle
            || debug.ShowTiming
            || debug.ShowConfidence
            || debug.ShowRawOcrText
            || debug.ShowParsedCoordinates;

        return debug.Enabled && hud.Visible && anySectionSelected;
    }

    public static IReadOnlyList<HudLine> BuildLines(DebugSnapshot snapshot, DebugSettings settings)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(settings);

        if (!settings.Enabled)
        {
            return [];
        }

        var lines = new List<HudLine>(10);

        if (settings.ShowCursorAnchor)
        {
            lines.Add(Line("CURSOR", $"{snapshot.CursorX},{snapshot.CursorY}", HudSegmentKind.Secondary));
        }

        if (settings.ShowRoiRectangle)
        {
            lines.Add(Line("ROI", "", HudSegmentKind.Secondary));
            lines.Add(Line("  X", snapshot.Roi.X.ToString(CultureInfo.InvariantCulture), HudSegmentKind.Primary));
            lines.Add(Line("  Y", snapshot.Roi.Y.ToString(CultureInfo.InvariantCulture), HudSegmentKind.Primary));
            lines.Add(Line("  W", snapshot.Roi.Width.ToString(CultureInfo.InvariantCulture), HudSegmentKind.Primary));
            lines.Add(Line("  H", snapshot.Roi.Height.ToString(CultureInfo.InvariantCulture), HudSegmentKind.Primary));
        }

        if (settings.ShowTiming)
        {
            lines.Add(Line("OCR", $"{snapshot.OcrMilliseconds:0.0}ms", HudSegmentKind.Secondary));
            lines.Add(Line("TOTAL", $"{snapshot.TotalMilliseconds:0.0}ms", HudSegmentKind.Secondary));
        }

        if (settings.ShowConfidence)
        {
            lines.Add(Line("CONF", snapshot.Confidence.ToString("0.00", CultureInfo.InvariantCulture),
                HudSegmentKind.Secondary));
        }

        if (settings.ShowRawOcrText)
        {
            lines.Add(Line("RAW", "", HudSegmentKind.Secondary));

            foreach (var raw in SplitLines(snapshot.RawText))
            {
                lines.Add(Line("", raw, HudSegmentKind.Primary));
            }
        }

        if (settings.ShowParsedCoordinates)
        {
            if (snapshot.Success && snapshot.Coordinate is { } coordinate)
            {
                lines.Add(Line("X", coordinate.X.ToString("0.00", CultureInfo.InvariantCulture),
                    HudSegmentKind.Primary));
                lines.Add(Line("Y", coordinate.Y.ToString("0.00", CultureInfo.InvariantCulture),
                    HudSegmentKind.Primary));
            }
            else
            {
                lines.Add(Line("FAIL", snapshot.Error ?? "UNKNOWN", HudSegmentKind.Status));
            }
        }

        foreach (var summary in snapshot.PipelineSummary)
        {
            lines.Add(Line("", summary, HudSegmentKind.Secondary));
        }

        return lines;
    }

    /// <summary>Debug 面板用固定的小字号，免得挡住游戏视野。</summary>
    public static HudTheme BuildDebugTheme(HudTheme source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var theme = source.Clone();
        theme.Layout = HudLayout.Detailed;
        theme.FontSize = Math.Max(11, source.FontSize * 0.6);
        theme.LineHeight = 1.05;
        theme.Outline = OutlineWeight.Thin;
        theme.Shadow = ShadowMode.Soft;
        theme.Background = HudBackgroundMode.TransparentPanel;
        theme.BackgroundColor = "#A0000000";
        theme.Padding = 6;
        theme.CornerRadius = 4;
        return theme;
    }

    private static HudLine Line(string label, string value, HudSegmentKind kind)
        => new([new HudSegment(label, value, kind) { Gap = string.IsNullOrEmpty(label) ? 0 : 1 }]);

    private static IEnumerable<string> SplitLines(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            yield return Loc.T("Empty2");
            yield break;
        }

        foreach (var line in text.Replace("\r", "").Split('\n'))
        {
            if (line.Length > 0)
            {
                yield return line;
            }
        }
    }
}
