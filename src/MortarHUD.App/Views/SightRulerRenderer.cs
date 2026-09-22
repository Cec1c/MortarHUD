using System.Globalization;
using System.Windows;
using System.Windows.Media;
using MortarHUD.Core.Ballistics;
using MortarHUD.Core.Configuration;

namespace MortarHUD.App.Views;

public sealed class SightRulerRenderer : FrameworkElement
{
    private RulerProfile _profile = new();
    private double? _range;
    private bool _preview;
    private string _note = "";
    private static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(104, 235, 239));

    public void Update(RulerProfile profile, double? range, bool preview = false, string note = "")
    {
        _profile = profile;
        _range = range;
        _preview = preview;
        _note = note;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (ActualWidth <= 0 || ActualHeight <= 0 || !_profile.IsValid) return;
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight)));
        var scale = _preview ? Math.Min(ActualWidth / 400, ActualHeight / 570) : ActualHeight / _profile.Height;
        var originX = _preview ? ActualWidth * .33 : _profile.AxisX * scale;
        var originY = _preview ? ActualHeight * .5 : _profile.CenterY * scale;
        // 预览保持标签可读；运行时把物理像素一次性换成当前窗口的 DIP。
        var unit = _preview ? scale : scale * _profile.Height / 1080;
        var textSize = 15 * unit;
        void Text(string text, double x, double y, Brush color, double size = 0)
        {
            var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface("Microsoft YaHei UI"), size > 0 ? size : textSize, color, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            var geometry = formatted.BuildGeometry(new Point(x, y));
            dc.DrawGeometry(color, new Pen(Brushes.Black, 2.5 * unit) { LineJoin = PenLineJoin.Round }, geometry);
            dc.DrawGeometry(color, null, geometry);
        }
        void Line(double x1, double y1, double x2, double y2, Brush color, double thickness = 1)
        {
            dc.DrawLine(new Pen(Brushes.Black, (thickness + 2) * unit), new(x1, y1), new(x2, y2));
            dc.DrawLine(new Pen(color, thickness * unit), new(x1, y1), new(x2, y2));
        }
        var ticks = _range is { } range ? SightRuler.Build(range, _profile) : [];
        if (ticks.Count == 0)
        {
            Text(_range is null ? "标尺等待目标" : $"RNG {_range:0}m · 超出标定范围", originX, originY, Accent);
            Text("已标定 80–684m", originX, originY + 28 * unit, Brushes.White);
            dc.Pop();
            return;
        }
        var top = originY - 235 * unit;
        var bottom = originY + 235 * unit;
        if (_preview)
        {
            // 预览只示意如何对齐，运行中的实际刻线来自游戏。
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(27, 36, 40)), null, new Rect(0, 0, ActualWidth, ActualHeight));
            Text("游戏刻线", originX - 95 * unit, top - 24 * unit, Brushes.Gray, 12 * unit);
            Text("外置标尺", originX + 24 * unit, top - 24 * unit, Accent, 12 * unit);
        }
        Line(originX, top, originX, bottom, Brushes.White);
        foreach (var tick in ticks)
        {
            var y = originY + (tick.Y - _profile.CenterY) * (_preview ? unit * 1080 / _profile.Height : scale);
            if (!tick.IsReference && ticks.Any(t => t.IsReference && Math.Abs(t.Y - tick.Y) < 6 * _profile.Height / 1080)) continue;
            Line(originX, y, originX + (tick.IsReference ? 22 : 9) * unit, y,
                tick.IsReference ? Brushes.White : Brushes.Gray, tick.IsReference ? 1.5 : 1);
            if (!tick.IsReference) continue;
            // 目标标签放在标尺右侧更远处，避免接近 340 等刻度时相互遮挡。
            Text($"{tick.RangeMeters:0}", originX + 28 * unit, y - 11 * unit, Brushes.White);
            if (_preview)
            {
                Line(originX - 28 * unit, y, originX - 8 * unit, y, Brushes.Gray);
                Text($"{tick.RangeMeters:0}", originX - 86 * unit, y - 11 * unit, Brushes.Gray);
            }
        }
        // RNG 正好等于 340 等标注时，目标线不能从白色数字中间穿过去。
        Line(originX - 8 * unit, originY, originX + 22 * unit, originY, Accent, 2);
        Line(originX + 80 * unit, originY, originX + 155 * unit, originY, Accent, 2);
        Text($"RNG {_range:0}m", originX + 85 * unit, originY - 26 * unit, Accent, 17 * unit);
        Text("对齐同名刻线", originX, bottom + 8 * unit, Accent, 12 * unit);
        if (_note.Length > 0) Text(_note, originX, bottom + 29 * unit, Brushes.White, 11 * unit);
        dc.Pop();
    }
}
