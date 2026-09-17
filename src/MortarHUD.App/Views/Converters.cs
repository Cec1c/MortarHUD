using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using MortarHUD.Platform.Windows.Hotkeys;

namespace MortarHUD.App.Views;

/// <summary>bool → Visibility。true 显示，false 折叠。</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Visibility.Visible;
}

/// <summary>bool 取反。用于「勾了 A 就禁用 B」这类联动。</summary>
public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not true;
}

/// <summary>
/// 热键配置串 → 界面显示名。
/// </summary>
/// <remarks>
/// settings.json 里存的是 "MouseMiddle" 这种规范写法，
/// 但界面上应该显示「鼠标中键」。转换只做单向：输入框是只读的，
/// 录制逻辑自己写回配置串。
/// </remarks>
public sealed class HotkeyDisplayConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => HotkeyParser.TryParse(value as string, out var definition, out _)
            ? definition.ToDisplayString()
            : value as string ?? "";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>颜色字符串 → 画刷。列表里的主题色块用它。</summary>
public sealed class ColorHexToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var brush = new SolidColorBrush(HudRenderer.ParseColor(value as string));
        brush.Freeze();
        return brush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}
