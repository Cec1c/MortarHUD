using System.Globalization;
using System.Windows;
using System.Windows.Media;
using MortarHUD.Core.Session;
using MortarHUD.Core.Themes;

namespace MortarHUD.App.Views;

/// <summary>
/// HUD 的实际绘制。
/// </summary>
/// <remarks>
/// <para>
/// 不用 XAML 里堆 TextBlock 的写法，而是直接 <see cref="OnRender"/> 画：
/// WPF 没有内建的文字描边，用叠加多个偏移 TextBlock 去模拟会随字号变形，
/// 而 <see cref="FormattedText.BuildGeometry"/> 拿到的是字形轮廓，
/// 用它描边才是真正的描边——这也是「亮地图上也看得清」的关键（TDD §24）。
/// </para>
/// <para>
/// 阴影同理：把字形轮廓平移后用半透明色填充，得到的是干净的投影，
/// 而不是把文字糊成一团。
/// </para>
/// </remarks>
public sealed class HudRenderer : FrameworkElement
{
    private static readonly Typeface FallbackTypeface =
        new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    private IReadOnlyList<HudLine> _lines = [];
    private HudTheme _theme = HudThemeLibrary.CreateDefaultGreen();
    private string _statusText = "";
    private string _statusDetail = "";
    private MortarStatusKind _statusKind = MortarStatusKind.None;
    private double _statusOpacity = 1.0;

    public HudRenderer()
    {
        // HUD 只用来显示，不该接收命中测试（鼠标穿透在 Win32 层已处理，
        // 这里再挡一层，避免编辑模式下误拖到文字上）。
        IsHitTestVisible = false;
    }

    public HudTheme Theme
    {
        get => _theme;
        set
        {
            _theme = value ?? HudThemeLibrary.CreateDefaultGreen();
            InvalidateVisual();
        }
    }

    public MortarStatusKind StatusKind
    {
        get => _statusKind;
        set
        {
            _statusKind = value;
            InvalidateVisual();
        }
    }

    /// <summary>
    /// 状态提示的透明度，0 表示已经淡没（TDD §20 的「状态信息自动淡出」）。
    /// </summary>
    /// <remarks>
    /// 只作用在状态行上，核心的 AZ / RNG 始终按主题里的整体不透明度显示。
    /// </remarks>
    public double StatusOpacity
    {
        get => _statusOpacity;
        set
        {
            var clamped = Math.Clamp(value, 0.0, 1.0);
            if (Math.Abs(_statusOpacity - clamped) < 0.001)
            {
                return;
            }

            _statusOpacity = clamped;
            InvalidateVisual();
        }
    }

    public void Update(IReadOnlyList<HudLine> lines, string statusText, string statusDetail)
    {
        _lines = lines;
        _statusText = statusText;
        _statusDetail = statusDetail;

        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (_lines.Count == 0 && string.IsNullOrEmpty(_statusText))
        {
            return new Size(0, 0);
        }

        var maxWidth = 0.0;
        var totalHeight = 0.0;
        var pixelsPerDip = GetPixelsPerDip();

        foreach (var line in _lines)
        {
            var (lineWidth, lineHeight) = MeasureLine(line, pixelsPerDip);
            maxWidth = Math.Max(maxWidth, lineWidth);
            totalHeight += lineHeight;
        }

        if (!string.IsNullOrEmpty(_statusText))
        {
            var statusWidth = MeasureText(_statusText, StatusBrush(), pixelsPerDip);
            maxWidth = Math.Max(maxWidth, statusWidth);
            totalHeight += MeasureLineHeight(pixelsPerDip) * 1.35;
        }

        if (!string.IsNullOrEmpty(_statusDetail))
        {
            var detailWidth = MeasureText(_statusDetail, StatusBrush(), pixelsPerDip, 0.8);
            maxWidth = Math.Max(maxWidth, detailWidth);
            totalHeight += MeasureLineHeight(pixelsPerDip) * 0.95;
        }

        var padding = _theme.Background == HudBackgroundMode.None ? 0 : _theme.Padding;

        return new Size(maxWidth + padding * 2, totalHeight + padding * 2);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var pixelsPerDip = GetPixelsPerDip();
        var padding = _theme.Background == HudBackgroundMode.None ? 0 : _theme.Padding;

        var contentWidth = ActualWidth - padding * 2;
        var contentHeight = ActualHeight - padding * 2;

        if (contentWidth <= 0 || contentHeight <= 0)
        {
            return;
        }

        DrawBackground(drawingContext, padding);

        var y = padding;

        foreach (var line in _lines)
        {
            var (lineWidth, lineHeight) = MeasureLine(line, pixelsPerDip);
            var x = ResolveLineStart(lineWidth, contentWidth, padding);
            var baselineOffset = (lineHeight - MeasureLineHeight(pixelsPerDip)) / 2;

            foreach (var segment in line.Segments)
            {
                var brush = ResolveBrush(segment.Kind);

                if (string.IsNullOrEmpty(segment.Label))
                {
                    // 纯空白段（Horizontal 布局里用来分隔 AZ 与 RNG）。
                    x += MeasureText(segment.ToText(), brush, pixelsPerDip);
                    continue;
                }

                x = DrawText(drawingContext, segment.ToText(), brush, x, y + baselineOffset, pixelsPerDip);
            }

            y += lineHeight;
        }

        DrawStatus(drawingContext, pixelsPerDip, padding, contentWidth, ref y);
    }

    private void DrawBackground(DrawingContext drawingContext, double padding)
    {
        if (_theme.Background == HudBackgroundMode.None)
        {
            return;
        }

        var color = ParseColor(_theme.BackgroundColor);

        // 主题里给的 alpha 与 BackgroundOpacity 相乘：前者控制「什么颜色 + 多透明」，
        // 后者是设置页上那个更直观的滑杆。
        var alpha = (byte)Math.Clamp(color.A * _theme.BackgroundOpacity, 0, 255);

        var brush = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
        brush.Freeze();

        var radius = _theme.Background == HudBackgroundMode.SolidPanel ? _theme.CornerRadius : _theme.CornerRadius;

        drawingContext.DrawRoundedRectangle(
            brush,
            null,
            new Rect(0, 0, ActualWidth, ActualHeight),
            Math.Max(0, radius),
            Math.Max(0, radius));
    }

    private void DrawStatus(
        DrawingContext drawingContext, double pixelsPerDip, double padding, double contentWidth, ref double y)
    {
        if (string.IsNullOrEmpty(_statusText))
        {
            return;
        }

        var brush = StatusBrush();
        var statusHeight = MeasureLineHeight(pixelsPerDip) * 1.35;
        var statusWidth = MeasureText(_statusText, brush, pixelsPerDip);

        DrawText(
            drawingContext, _statusText, brush,
            ResolveLineStart(statusWidth, contentWidth, padding),
            y + statusHeight * 0.18,
            pixelsPerDip);

        y += statusHeight;

        if (string.IsNullOrEmpty(_statusDetail))
        {
            return;
        }

        // 「Target unchanged」这类副提示要明显更弱，否则会和主状态抢注意力。
        var detailBrush = new SolidColorBrush(Color.FromArgb(
            (byte)Math.Clamp(0xB0 * _statusOpacity, 0, 255), 0xD0, 0xD0, 0xD0));
        detailBrush.Freeze();

        var detailHeight = MeasureLineHeight(pixelsPerDip) * 0.95;
        var detailWidth = MeasureText(_statusDetail, detailBrush, pixelsPerDip, 0.8);

        DrawText(
            drawingContext, _statusDetail, detailBrush,
            ResolveLineStart(detailWidth, contentWidth, padding),
            y,
            pixelsPerDip,
            0.8);
    }

    /// <summary>画一段文字，返回下一个绘制位置的 x（支持字间距）。</summary>
    private double DrawText(
        DrawingContext drawingContext,
        string text,
        Brush brush,
        double x,
        double y,
        double pixelsPerDip,
        double scale = 1.0)
    {
        if (_theme.LetterSpacing <= 0.01 || text.Length <= 1)
        {
            var formatted = CreateText(text, brush, pixelsPerDip, scale);
            DrawFormattedText(drawingContext, formatted, brush, x, y);
            return x + formatted.WidthIncludingTrailingWhitespace;
        }

        // WPF 的 FormattedText 没有字间距，只能逐字画并手动加间距。
        var cursor = x;

        foreach (var character in text)
        {
            var formatted = CreateText(character.ToString(CultureInfo.InvariantCulture), brush, pixelsPerDip, scale);
            DrawFormattedText(drawingContext, formatted, brush, cursor, y);
            cursor += formatted.WidthIncludingTrailingWhitespace + _theme.LetterSpacing;
        }

        return cursor;
    }

    private void DrawFormattedText(
        DrawingContext drawingContext, FormattedText text, Brush brush, double x, double y)
    {
        var origin = new Point(x, y);
        var geometry = text.BuildGeometry(origin);

        if (geometry is null)
        {
            drawingContext.DrawText(text, origin);
            return;
        }

        if (_theme.Shadow != ShadowMode.Off)
        {
            var offset = _theme.Shadow == ShadowMode.Hard ? 2.0 : 1.2;
            var shadowColor = ParseColor(_theme.ShadowColor);

            var shadowBrush = new SolidColorBrush(shadowColor);
            shadowBrush.Freeze();

            var transform = new TranslateTransform(offset, offset);
            drawingContext.PushTransform(transform);
            drawingContext.DrawGeometry(shadowBrush, null, geometry);
            drawingContext.Pop();
        }

        if (_theme.Outline != OutlineWeight.Off)
        {
            var outlineColor = ParseColor(_theme.OutlineColor);
            var outlineBrush = new SolidColorBrush(outlineColor);
            outlineBrush.Freeze();

            var thickness = _theme.Outline switch
            {
                OutlineWeight.Thin => _theme.OutlineThickness,
                OutlineWeight.Medium => _theme.OutlineThickness * 1.4,
                OutlineWeight.Thick => _theme.OutlineThickness * 2.0,
                _ => 0.0,
            };

            var pen = new Pen(outlineBrush, thickness)
            {
                LineJoin = PenLineJoin.Round,
                MiterLimit = 2,
            };
            pen.Freeze();

            // 描边用「先描轮廓再填字」，所以这里画的是几何体的边界。
            drawingContext.DrawGeometry(null, pen, geometry);
        }

        drawingContext.DrawGeometry(brush, null, geometry);
    }

    private FormattedText CreateText(string text, Brush brush, double pixelsPerDip, double scale = 1.0)
    {
        var weight = ParseWeight(_theme.FontWeight);
        var style = _theme.Italic ? FontStyles.Italic : FontStyles.Normal;

        var typeface = ResolveTypeface(_theme.FontFamily, style, weight);

        return new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            typeface,
            Math.Max(1.0, _theme.FontSize * scale),
            brush,
            pixelsPerDip);
    }

    /// <summary>
    /// 解析字体，找不到就退回系统 UI 字体。
    /// </summary>
    /// <remarks>
    /// <para>
    /// TDD §23 明确要求「不应依赖程序附带第三方字体文件」——
    /// 用户机器上没装 Cascadia Mono 是完全正常的，此时必须还能用。
    /// </para>
    /// <para>
    /// 主题字体（默认 Cascadia Mono）是等宽的，数字不会跳宽，但它<strong>没有中文字形</strong>，
    /// 而 HUD 的状态行是中文（「炮位已锁定」这类）。不给后备的话，汉字会落到 WPF
    /// 自己的复合字体链上——落到哪个字体、什么字重都不受控，同一块 HUD 里
    /// 数字和汉字就一副粗一副细。这里把后备钉成界面用的那套字体，
    /// 两种文字至少是同一个设计。
    /// </para>
    /// </remarks>
    private static Typeface ResolveTypeface(string familyName, FontStyle style, FontWeight weight)
    {
        if (string.IsNullOrWhiteSpace(familyName))
        {
            return FallbackTypeface;
        }

        try
        {
            // FontFamily 支持逗号分隔的后备列表，WPF 按字形逐个回退。
            var family = new FontFamily($"{familyName}, {CjkFallbackFamily}");
            var typeface = new Typeface(family, style, weight, FontStretches.Normal);
            return typeface.FontFamily.FamilyNames.Count > 0 ? typeface : FallbackTypeface;
        }
        catch (ArgumentException)
        {
            return FallbackTypeface;
        }
    }

    /// <summary>HUD 里中文的后备字体。</summary>
    /// <remarks>
    /// 挑 Microsoft YaHei UI 是因为它就是设置界面的字体，两处看起来是一套东西；
    /// 而且从 Vista 起每台 Windows 都自带，不违反 TDD §23。
    /// </remarks>
    private const string CjkFallbackFamily = "Microsoft YaHei UI";

    private static FontWeight ParseWeight(string? name) => name switch
    {
        "Light" => FontWeights.Light,
        "Normal" or "Regular" => FontWeights.Normal,
        "Medium" => FontWeights.Medium,
        "SemiBold" or "DemiBold" => FontWeights.SemiBold,
        "Bold" => FontWeights.Bold,
        "Black" or "Heavy" => FontWeights.Black,
        _ => FontWeights.SemiBold,
    };

    private Brush ResolveBrush(HudSegmentKind kind) => kind switch
    {
        HudSegmentKind.Secondary => CreateFrozenBrush(_theme.SecondaryColor),
        HudSegmentKind.Status => StatusBrush(),
        _ => CreateFrozenBrush(_theme.PrimaryColor),
    };

    private Brush StatusBrush()
    {
        var color = ParseColor(_statusKind switch
        {
            MortarStatusKind.OcrFailed or MortarStatusKind.InvalidCoordinate or MortarStatusKind.CaptureFailed
                => _theme.ErrorColor,
            // 「什么都没发生」用警告色而不是错误色：数据没被污染，只是这次没采到。
            MortarStatusKind.NoGunPosition or MortarStatusKind.CaptureCancelled
                or MortarStatusKind.AutoCalibrateSkipped => _theme.WarningColor,
            _ => _theme.SuccessColor,
        });

        var faded = new SolidColorBrush(Color.FromArgb(
            (byte)Math.Clamp(color.A * _statusOpacity, 0, 255), color.R, color.G, color.B));
        faded.Freeze();
        return faded;
    }

    private static Brush CreateFrozenBrush(string hex)
    {
        var brush = new SolidColorBrush(ParseColor(hex));
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// 解析 <c>#RRGGBB</c> / <c>#AARRGGBB</c>（TDD §22 允许 HEX / RGB / ARGB）。
    /// </summary>
    public static Color ParseColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            return Colors.White;
        }

        var text = hex.Trim().TrimStart('#');

        // RGB 简写补成 RRGGBB。
        if (text.Length == 3)
        {
            text = string.Concat(text.Select(c => new string(c, 2)));
        }

        if (text.Length == 6)
        {
            text = "FF" + text;
        }

        if (text.Length != 8
            || !uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
        {
            return Colors.White;
        }

        return Color.FromArgb(
            (byte)((value >> 24) & 0xFF),
            (byte)((value >> 16) & 0xFF),
            (byte)((value >> 8) & 0xFF),
            (byte)(value & 0xFF));
    }

    private double MeasureLineHeight(double pixelsPerDip)
        => CreateText("Ag", Brushes.White, pixelsPerDip).Height * _theme.LineHeight;

    private (double Width, double Height) MeasureLine(HudLine line, double pixelsPerDip)
    {
        if (line.Segments.Count == 0)
        {
            return (0, MeasureLineHeight(pixelsPerDip));
        }

        var width = line.Segments.Sum(s => MeasureText(s.ToText(), Brushes.White, pixelsPerDip));

        return (width, MeasureLineHeight(pixelsPerDip));
    }

    /// <summary>
    /// 测量一段文字的宽度。
    /// </summary>
    /// <remarks>
    /// 开了字间距时必须逐字累加——<see cref="FormattedText"/> 本身不认识字间距，
    /// 直接量整串会偏窄，导致居中对齐时文字整体偏左。
    /// </remarks>
    private double MeasureText(string text, Brush brush, double pixelsPerDip, double scale = 1.0)
    {
        if (_theme.LetterSpacing <= 0.01 || text.Length <= 1)
        {
            return CreateText(text, brush, pixelsPerDip, scale).WidthIncludingTrailingWhitespace;
        }

        var width = 0.0;

        foreach (var character in text)
        {
            width += CreateText(character.ToString(CultureInfo.InvariantCulture), brush, pixelsPerDip, scale)
                         .WidthIncludingTrailingWhitespace
                     + _theme.LetterSpacing;
        }

        // 末尾多算了一个间距，减掉。
        return Math.Max(0, width - _theme.LetterSpacing);
    }

    private double ResolveLineStart(double lineWidth, double contentWidth, double padding) => _theme.Alignment switch
    {
        HudTextAlignment.Center => padding + (contentWidth - lineWidth) / 2,
        HudTextAlignment.Right => padding + (contentWidth - lineWidth),
        _ => padding,
    };

    private double GetPixelsPerDip()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        return dpi.PixelsPerDip > 0 ? dpi.PixelsPerDip : 1.0;
    }
}
