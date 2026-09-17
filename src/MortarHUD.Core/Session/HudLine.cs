namespace MortarHUD.Core.Session;

/// <summary>一段文字在 HUD 里的语义角色，决定用哪个颜色渲染。</summary>
public enum HudSegmentKind
{
    /// <summary>强调色（数值）。</summary>
    Primary,

    /// <summary>次级色（标签、炮位/目标坐标）。</summary>
    Secondary,

    /// <summary>状态提示色。</summary>
    Status,
}

public sealed record HudSegment(string Label, string Value, HudSegmentKind Kind = HudSegmentKind.Primary)
{
    /// <summary>标签与数值之间的空格数，用于让各行的数值对齐。</summary>
    public int Gap { get; init; } = 1;

    /// <summary>
    /// 拼成一行文本。
    /// </summary>
    /// <remarks>
    /// 没有标签时不能再补分隔空格——Minimal 布局只有数值，
    /// 多一个前导空格会让整行相对锚点偏移，居中对齐时尤其明显。
    /// </remarks>
    public string ToText()
        => string.IsNullOrEmpty(Label) || Gap <= 0
            ? $"{Label}{Value}"
            : $"{Label}{new string(' ', Gap)}{Value}";
}

/// <summary>HUD 的一行。</summary>
public sealed record HudLine(IReadOnlyList<HudSegment> Segments)
{
    public static readonly HudLine Empty = new([]);

    public string ToText() => string.Concat(Segments.Select(s => s.ToText()));
}
