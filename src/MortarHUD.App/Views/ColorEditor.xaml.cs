using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MortarHUD.App.Views;

/// <summary>
/// 颜色编辑器（TDD §22）：HEX 输入 + 颜色预览 + 常用色板 + 不透明度滑杆。
/// </summary>
/// <remarks>
/// <para>
/// 接受 <c>#RRGGBB</c>、<c>#AARRGGBB</c>、<c>RRGGBB</c>、<c>AARRGGBB</c>
/// 以及 <c>#RGB</c> 简写——TDD §22 要求 HEX / RGB / ARGB 都能用。
/// </para>
/// <para>
/// 颜色值统一用字符串保存，因为它要原样写进 settings.json 和主题文件。
/// 存 <see cref="Color"/> 结构反而要在序列化时来回转换。
/// </para>
/// </remarks>
public partial class ColorEditor : UserControl
{
    /// <summary>色板里的常用颜色。覆盖 HUD 最常用的几种高对比配色。</summary>
    private static readonly string[] PaletteColors =
    [
        "#7CFF6B", "#B8FFAF", "#00FF88", "#00E5FF", "#4FC3F7", "#7C9CFF",
        "#FFFFFF", "#D6D6D6", "#9AA0A6", "#000000",
        "#FFD866", "#FFB000", "#FF8A3D", "#FF6464", "#FF3B3B", "#FF6BD6",
        "#C77DFF", "#8E6BFF", "#00C853", "#1DE9B6", "#FFEB3B", "#795548",
    ];

    public static readonly DependencyProperty ColorHexProperty =
        DependencyProperty.Register(
            nameof(ColorHex),
            typeof(string),
            typeof(ColorEditor),
            new FrameworkPropertyMetadata(
                "#FFFFFF",
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnColorHexChanged));

    public ColorEditor()
    {
        InitializeComponent();
        BuildSwatches();

        // 构造函数尾部才挂事件：XAML 里带默认值的事件绑定会在 InitializeComponent()
        // 期间就引发一次，那时本类的具名字段还没全部就位。
        AlphaSlider.ValueChanged += OnAlphaChanged;
    }

    /// <summary>当前颜色的十六进制字符串。</summary>
    public string ColorHex
    {
        get => (string)GetValue(ColorHexProperty);
        set => SetValue(ColorHexProperty, value);
    }

    private static void OnColorHexChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ColorEditor editor)
        {
            return;
        }

        editor.Swatch.Background = new SolidColorBrush(HudRenderer.ParseColor(e.NewValue as string));

        // 滑杆跟随当前的 alpha，避免用户一进来拖滑杆就把 alpha 从 128 拉到 255。
        var color = HudRenderer.ParseColor(e.NewValue as string);
        if (Math.Abs(editor.AlphaSlider.Value - color.A) > 0.5)
        {
            editor.AlphaSlider.Value = color.A;
        }
    }

    private void BuildSwatches()
    {
        foreach (var hex in PaletteColors)
        {
            var button = new Button
            {
                Width = 24,
                Height = 24,
                Margin = new Thickness(0, 0, 4, 4),
                Padding = new Thickness(0),
                Background = new SolidColorBrush(HudRenderer.ParseColor(hex)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x3D, 0x42)),
                BorderThickness = new Thickness(1),
                ToolTip = hex,
                Tag = hex,
            };

            button.Click += (_, _) =>
            {
                // 点色板只改 RGB，保留用户已经调好的不透明度。
                var current = HudRenderer.ParseColor(ColorHex);
                var picked = HudRenderer.ParseColor((string)button.Tag);
                ColorHex = $"#{current.A:X2}{picked.R:X2}{picked.G:X2}{picked.B:X2}";
            };

            SwatchList.Items.Add(button);
        }
    }

    private void OnAlphaChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        var alpha = (byte)Math.Clamp(e.NewValue, 0, 255);
        var current = HudRenderer.ParseColor(ColorHex);

        var updated = $"#{alpha:X2}{current.R:X2}{current.G:X2}{current.B:X2}";

        if (!string.Equals(updated, ColorHex, StringComparison.OrdinalIgnoreCase))
        {
            ColorHex = updated;
        }
    }

    /// <summary>校验一个字符串是不是合法颜色，供设置页做即时反馈。</summary>
    public static bool IsValidColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            return false;
        }

        var text = hex.Trim().TrimStart('#');
        if (text.Length == 3)
        {
            text = string.Concat(text.Select(c => new string(c, 2)));
        }

        if (text.Length is not (6 or 8))
        {
            return false;
        }

        return uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _);
    }
}
