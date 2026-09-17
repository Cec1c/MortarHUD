using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using MortarHUD.App.ViewModels;
using MortarHUD.Capture;
using MortarHUD.Core.Ballistics;
using MortarHUD.Core.Configuration;
using MortarHUD.Core.Models;
using MortarHUD.Core.Session;
using MortarHUD.Platform.Windows.Hotkeys;
using HudMouseButton = MortarHUD.Platform.Windows.Hotkeys.MouseButton;
using Microsoft.Win32;

namespace MortarHUD.App.Views;

/// <summary>
/// 设置窗口（TDD §27-§33）。
/// </summary>
/// <remarks>
/// 交互全部走 <see cref="SettingsViewModel"/>；窗口本身只负责
/// 「弹对话框、跑测试采集、把预览画出来」这些纯 UI 的活。
/// </remarks>
public partial class SettingsWindow : Window
{
    private readonly MortarHudSettings _settings;
    private readonly Func<Task> _onApply;
    private readonly Action<int>? _collectDiagnostics;
    private readonly Func<Task<CaptureOutcome>> _testCapture;
    private readonly SettingsViewModel _viewModel;

    private TextBox? _recordingHotkeyBox;
    private string? _hotkeyBeforeRecording;

    public SettingsWindow(
        MortarHudSettings settings,
        Func<Task> onApply,
        Func<Task<CaptureOutcome>> testCapture, Action<int>? collectDiagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _settings = settings;
        _onApply = onApply;
        _testCapture = testCapture;
        _collectDiagnostics = collectDiagnostics;

        InitializeComponent();

        _viewModel = new SettingsViewModel(settings);
        _viewModel.Changed += OnViewModelChanged;
        DataContext = _viewModel;

        // 事件在代码里挂，不写在 XAML 上。
        // XAML 里带默认值的事件绑定会在 InitializeComponent() 期间就引发一次，
        // 那时这个类的字段还全是 null —— 这正是之前启动即崩溃的原因。
        LivePreviewCheck.Checked += OnLivePreviewToggled;
        LivePreviewCheck.Unchecked += OnLivePreviewToggled;

        // 立刻画一次预览：不依赖 Loaded，窗口一构造出来预览区就有内容。
        // （离屏渲染时 Loaded 根本不会触发，那时也要能看到预览。）
        RefreshPreview();

        // 加载完成后再画一次：这时才拿到最终的 DPI 与可用尺寸。
        Loaded += (_, _) => RefreshPreview();
    }

    private void OnViewModelChanged(object? sender, EventArgs e)
    {
        if (LivePreviewCheck.IsChecked == true)
        {
            RefreshPreview();
        }
    }

    // ================================================================ 预览

    /// <summary>
    /// 重画右侧预览。
    /// </summary>
    /// <remarks>
    /// 预览用的是 TDD §9 / §20 里那组示例数（炮位 98.09/109.78、目标 99.58/110.07，
    /// 解算出约 152m / 079.0°）。用它而不是随便编一个数，是为了让用户一眼能看出
    /// 「显示出来的格式对不对」——特别是方位角的补零和距离的取整。
    /// </remarks>
    private void RefreshPreview()
    {
        var preview = _viewModel.BuildPreviewSettings();

        var calculator = new MortarCalculator(
            preview.General.MetersPerCoordinateUnit,
            preview.General.XPositiveDirection != "West",
            preview.General.YPositiveDirection != "South");

        var session = new MortarSession(calculator);
        session.LockGun(new MapCoordinate(98.09, 109.78));
        session.LockTarget(new MapCoordinate(99.58, 110.07));

        PreviewRenderer.Theme = preview.Hud.CurrentTheme;
        PreviewRenderer.StatusKind = MortarStatusKind.None;
        PreviewRenderer.Update(HudLayoutFormatter.BuildLines(session, preview.Hud), "", "");

        // 预览面板尺寸跟着内容走，超出就让它被裁掉——这本身也是在提示用户字号太大了。
        PreviewRenderer.MinWidth = 240;
        PreviewRenderer.MinHeight = 90;
    }

    // ================================================================ 热键录制

    private void OnHotkeyBoxGotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox box)
        {
            return;
        }

        _recordingHotkeyBox = box;
        _hotkeyBeforeRecording = box.Text;
        box.Text = "请按键或鼠标键…";

        // 被录制的那个键要拦下来，否则按下 F6 会直接把当前值又填回去。
        box.PreviewKeyDown -= OnHotkeyBoxKeyDown;
        box.PreviewKeyDown += OnHotkeyBoxKeyDown;
    }

    private void OnHotkeyBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox box)
        {
            return;
        }

        // 只按修饰键不算一次完整录制，继续等主键。
        if (IsModifierKey(e.Key))
        {
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            box.Text = _hotkeyBeforeRecording ?? "";
            box.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            e.Handled = true;
            return;
        }

        var definition = BuildDefinition(Keyboard.Modifiers, e.Key);

        if (!definition.IsValid)
        {
            box.Text = "这个键不支持";
            e.Handled = true;
            return;
        }

        box.Text = definition.ToDisplayString();
        ApplyHotkeyText(box, definition.ToConfigString());

        box.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
        e.Handled = true;
    }

    private void ApplyHotkeyText(TextBox box, string text)
    {
        if (ReferenceEquals(box, GunHotkeyBox))
        {
            _viewModel.CaptureGun = text;
        }
        else if (ReferenceEquals(box, TargetHotkeyBox))
        {
            _viewModel.CaptureTarget = text;
        }
        else if (ReferenceEquals(box, ToggleHotkeyBox))
        {
            _viewModel.ToggleHudHotkey = text;
        }
        else if (ReferenceEquals(box, SettingsHotkeyBox))
        {
            _viewModel.OpenSettingsHotkey = text;
        }
        else if (ReferenceEquals(box, MapKeyBox))
        {
            _viewModel.AutoCalibrateKey = text;
        }
    }

    /// <summary>
    /// 录制鼠标键。
    /// </summary>
    /// <remarks>
    /// 要求输入框<strong>已经获得焦点</strong>之后才录制：
    /// 否则第一次左键点击会先去聚焦、同时又被录成「鼠标左键」，
    /// 用户就再也没法用鼠标点进这个框了。
    /// 所以第一次点击只是聚焦，第二次点击才录制。
    /// </remarks>
    private void OnHotkeyBoxMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!IsLoaded || _viewModel is null || sender is not TextBox box || !box.IsFocused)
        {
            return;
        }

        var button = e.ChangedButton switch
        {
            System.Windows.Input.MouseButton.Left => HudMouseButton.Left,
            System.Windows.Input.MouseButton.Right => HudMouseButton.Right,
            System.Windows.Input.MouseButton.Middle => HudMouseButton.Middle,
            System.Windows.Input.MouseButton.XButton1 => HudMouseButton.X1,
            System.Windows.Input.MouseButton.XButton2 => HudMouseButton.X2,
            _ => (HudMouseButton)0,
        };

        if (button == (HudMouseButton)0)
        {
            return;
        }

        var definition = new HotkeyDefinition(
            BuildModifierFlags(Keyboard.Modifiers), (uint)button, HotkeyDevice.Mouse);

        box.Text = definition.ToDisplayString();
        ApplyHotkeyText(box, definition.ToConfigString());

        box.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
        e.Handled = true;
    }

    private static uint BuildModifierFlags(ModifierKeys modifiers)
    {
        uint flags = 0;

        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            flags |= 0x0002;
        }

        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            flags |= 0x0001;
        }

        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            flags |= 0x0004;
        }

        if (modifiers.HasFlag(ModifierKeys.Windows))
        {
            flags |= 0x0008;
        }

        return flags;
    }

    private static bool IsModifierKey(Key key)
        => key is Key.LeftCtrl or Key.RightCtrl
            or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin
            or Key.System;

    /// <summary>把 WPF 的按键组合翻成项目的热键定义。</summary>
    private static HotkeyDefinition BuildDefinition(ModifierKeys modifiers, Key key)
        => new(BuildModifierFlags(modifiers), (uint)KeyInterop.VirtualKeyFromKey(key));

    private void OnRestoreHotkeysClicked(object sender, RoutedEventArgs e)
    {
        var defaults = HotkeySettings.CreateDefault();

        _viewModel.CaptureGun = defaults.CaptureGun;
        _viewModel.CaptureTarget = defaults.CaptureTarget;
        _viewModel.ToggleHudHotkey = defaults.ToggleHud;
        _viewModel.OpenSettingsHotkey = defaults.OpenSettings;
        _viewModel.AutoCalibrateEnabled = defaults.AutoCalibrateEnabled;
        _viewModel.AutoCalibrateKey = defaults.AutoCalibrateKey;
        _viewModel.AutoCalibrateDelayMs = defaults.AutoCalibrateDelayMs;

        StatusText.Text = "热键已恢复默认，点「应用」后生效。";
    }

    // ================================================================ HUD 位置

    private async void OnUnlockPositionToggled(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        StatusText.Text = UnlockPositionCheck.IsChecked == true
            ? "HUD 已解锁：直接拖动屏幕上的 HUD 调整位置，调好后取消勾选即可恢复鼠标穿透。"
            : "HUD 已锁定，恢复鼠标穿透。";

        _settings.Hud.PositionUnlocked = _viewModel.PositionUnlocked;
        try { await _onApply(); }
        catch (Exception ex) { Warn($"应用失败：{ex.Message}"); }
    }

    private void OnResetPositionClicked(object sender, RoutedEventArgs e)
    {
        _viewModel.Anchor = Core.Themes.HudAnchor.CenterLeft;
        _viewModel.OffsetX = 40;
        _viewModel.OffsetY = 0;

        StatusText.Text = "位置已重置为默认（Center Left，偏移 40, 0）。";
    }

    // ================================================================ 主题

    private void OnApplyThemeClicked(object sender, RoutedEventArgs e)
    {
        _viewModel.ApplySelectedTheme();
        StatusText.Text = $"已应用主题「{_viewModel.WorkingTheme.Name}」，点「应用」写入设置。";
    }

    private void OnDuplicateThemeClicked(object sender, RoutedEventArgs e)
    {
        var name = PromptForText("另存为主题", "新主题名称：", $"{_viewModel.WorkingTheme.Name} 副本");
        if (name is null)
        {
            return;
        }

        var saved = _viewModel.SaveAsNewTheme(name);
        StatusText.Text = $"已保存自定义主题「{saved.Name}」。";
    }

    private void OnOverwriteThemeClicked(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedTheme?.IsBuiltIn != false)
        {
            Warn("内置主题不能覆盖保存，请用「另存为」。");
            return;
        }

        _viewModel.OverwriteSelectedTheme();
        StatusText.Text = $"已覆盖保存主题「{_viewModel.SelectedTheme?.Name}」。";
    }

    private void OnRenameThemeClicked(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedTheme is null)
        {
            return;
        }

        if (_viewModel.SelectedTheme.IsBuiltIn)
        {
            Warn("内置主题不能改名。可以先「另存为」再改。");
            return;
        }

        var name = PromptForText("重命名主题", "新名称：", _viewModel.SelectedTheme.Name);
        if (name is null)
        {
            return;
        }

        if (!_viewModel.RenameSelectedTheme(name, out var error))
        {
            Warn(error ?? "重命名失败。");
            return;
        }

        StatusText.Text = $"主题已重命名为「{name}」。";
    }

    private void OnDeleteThemeClicked(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedTheme is null)
        {
            return;
        }

        var confirm = MessageBox.Show(
            $"确定要删除主题「{_viewModel.SelectedTheme.Name}」吗？",
            "MortarHUD",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.OK)
        {
            return;
        }

        if (!_viewModel.DeleteSelectedTheme(out var error))
        {
            Warn(error ?? "删除失败。");
            return;
        }

        StatusText.Text = "主题已删除。";
    }

    private void OnImportThemeClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "导入主题",
            Filter = "MortarHUD 主题 (*.json)|*.json|所有文件 (*.*)|*.*",
            InitialDirectory = AppPaths.ThemesDirectory,
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        if (!_viewModel.ImportTheme(dialog.FileName, out var error))
        {
            Warn(error ?? "导入失败。");
            return;
        }

        StatusText.Text = "主题已导入。";
    }

    private void OnExportThemeClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "导出主题",
            Filter = "MortarHUD 主题 (*.json)|*.json",
            FileName = $"{_viewModel.WorkingTheme.Name}.json",
            InitialDirectory = AppPaths.ThemesDirectory,
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        if (!_viewModel.ExportTheme(dialog.FileName, out var error))
        {
            Warn(error ?? "导出失败。");
            return;
        }

        StatusText.Text = $"主题已导出到 {dialog.FileName}";
    }

    // ================================================================ Test OCR

    private async void OnTestOcrClicked(object sender, RoutedEventArgs e)
    {
        if (_testCapture is null)
        {
            return;
        }

        var button = sender as Button;
        if (button is not null) button.IsEnabled = false;
        try
        {
            for (var seconds = 3; seconds > 0; seconds--)
            {
                StatusText.Text = $"{seconds} 秒后采集：请切回游戏，指向坐标读数并保持不动。";
                await Task.Delay(1000);
            }
            _collectDiagnostics?.Invoke(1);
            var outcome = await _testCapture();

            TestRawImage.Source = null;
            TestProcessedImage.Source = null;

            var report = new System.Text.StringBuilder();
            report.AppendLine($"光标      {outcome.Cursor.X}, {outcome.Cursor.Y}");
            report.AppendLine($"ROI       X={outcome.Roi.X} Y={outcome.Roi.Y} "
                              + $"W={outcome.Roi.Width} H={outcome.Roi.Height}");
            report.AppendLine();

            if (outcome.Success && outcome.Coordinate is { } coordinate)
            {
                report.AppendLine("PASS");
                report.AppendLine($"X {coordinate.X:0.00}");
                report.AppendLine($"Y {coordinate.Y:0.00}");
            }
            else
            {
                report.AppendLine("FAIL");
                report.AppendLine($"原因：{outcome.Recognition.Error}");
            }

            report.AppendLine();
            report.AppendLine($"置信度    {outcome.Recognition.Confidence:0.00}");
            report.AppendLine($"Capture   {outcome.CaptureTime.TotalMilliseconds:0.0} ms");
            report.AppendLine($"Total     {outcome.TotalTime.TotalMilliseconds:0.0} ms");

            report.AppendLine();
            report.AppendLine("各流水线：");

            foreach (var attempt in outcome.Recognition.Attempts)
            {
                report.AppendLine($"  {attempt.Engine}/{attempt.Pipeline}  "
                                  + $"{(attempt.Success ? "OK" : attempt.Error)}");
                report.AppendLine($"    原始文本: {Describe(attempt.RawText)}");
                report.AppendLine($"    修正文本: {Describe(attempt.RepairedText)}");
                report.AppendLine($"    置信度 {attempt.Confidence:0.00}  "
                                  + $"预处理 {attempt.PreprocessTime.TotalMilliseconds:0.0}ms  "
                                  + $"OCR {attempt.OcrTime.TotalMilliseconds:0.0}ms");
            }

            TestResultText.Text = report.ToString();
            StatusText.Text = outcome.Success
                ? $"测试通过：X {outcome.Coordinate!.Value.X:0.00} / Y {outcome.Coordinate.Value.Y:0.00}"
                : $"测试失败：{outcome.Recognition.Error}";

            await LoadTestImagesAsync(outcome);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"测试出错：{ex.Message}";
        }
        finally
        {
            if (button is not null) button.IsEnabled = true;
        }
    }

    /// <summary>
    /// 把这次采集的中间图显示出来。
    /// </summary>
    /// <remarks>
    /// 目前只回读 Debug 目录里刚写下的图——那需要用户额外勾选「保存原始 ROI」。
    /// 更直接的做法是让采集链路把 Mat 回传给 UI，但那条路径会跨越线程边界，
    /// 为此把 Mat 的生存期管理复杂化不划算，所以这里退一步：
    /// 有转储就显示，没有就说清楚怎么打开。
    /// </remarks>
    private Task LoadTestImagesAsync(CaptureOutcome outcome)
    {
        TestRawLabel.Text = outcome.RawImagePath is null ? "本次未保存原图，请先开启诊断收集" : "本次原始 ROI";
        TestRawImage.Source = outcome.RawImagePath is { } raw ? LoadBitmap(raw) : null;
        TestProcessedImage.Source = outcome.ProcessedImagePath is { } processed ? LoadBitmap(processed) : null;
        return Task.CompletedTask;
    }

    private static BitmapImage? LoadBitmap(string path)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(path);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UriFormatException)
        {
            return null;
        }
    }

    private static string Describe(string text)
        => string.IsNullOrWhiteSpace(text)
            ? "（空）"
            : text.Replace("\r", "").Replace("\n", " | ").Trim();

    private void OnCollectDiagnosticsClicked(object sender, RoutedEventArgs e)
    {
        _collectDiagnostics?.Invoke(10);
        StatusText.Text = "已开启：接下来 10 次采集保存完整诊断。返回游戏后按热键复现即可。";
    }

    private void OnOpenDebugFolderClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            AppPaths.EnsureDirectories();
            Process.Start(new ProcessStartInfo
            {
                FileName = AppPaths.DebugDirectory,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Warn($"打开目录失败：{ex.Message}");
        }
    }

    // ================================================================ 应用 / 取消

    private void OnLivePreviewToggled(object sender, RoutedEventArgs e)
    {
        // 这个事件会在 InitializeComponent() 期间就被触发一次：
        // XAML 里「实时预览」写的是 IsChecked="True"，解析到这一行时 Checked 立刻引发，
        // 而此刻 _viewModel 与 PreviewRenderer 都还没构造出来。
        // 少了这个守卫，构造函数会抛 NullReferenceException，
        // 而调用方（App.OnStartup → ShowSettings）还没来得及显示任何窗口，程序就没了。
        if (!IsLoaded || _viewModel is null)
        {
            return;
        }

        if (LivePreviewCheck.IsChecked == true)
        {
            RefreshPreview();
        }
    }

    private async void OnApplyClicked(object sender, RoutedEventArgs e) => await ApplyAsync();

    private async Task<bool> ApplyAsync()
    {
        try
        {
            _viewModel.SaveTo(_settings);
            await _onApply();
            StatusText.Text = $"已应用（{DateTime.Now:HH:mm:ss}）。";
            return true;
        }
        catch (Exception ex)
        {
            Warn($"应用失败：{ex.Message}");
            return false;
        }
    }

    private async void OnOkClicked(object sender, RoutedEventArgs e)
    {
        if (await ApplyAsync()) Close();
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // TDD §36：关掉设置窗口只是收起来，程序继续在托盘里跑。
        e.Cancel = true;
        Hide();
    }

    // ================================================================ 小工具

    private void Warn(string message)
        => MessageBox.Show(message, "MortarHUD", MessageBoxButton.OK, MessageBoxImage.Warning);

    /// <summary>一个极简的文本输入框。WPF 没有内建的 InputBox，为它引一个库不值。</summary>
    private string? PromptForText(string title, string label, string defaultValue)
    {
        var window = new Window
        {
            Title = title,
            Width = 380,
            Height = 170,
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Background = (System.Windows.Media.Brush)FindResource("SurfaceBrush"),
        };

        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock
        {
            Text = label,
            Margin = new Thickness(0, 0, 0, 8),
            Foreground = (System.Windows.Media.Brush)FindResource("TextBrush"),
        });

        var input = new TextBox { Text = defaultValue, Padding = new Thickness(4) };
        panel.Children.Add(input);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0),
        };

        string? result = null;

        var ok = new Button { Content = "确定", Width = 76, IsDefault = true };
        ok.Click += (_, _) =>
        {
            result = input.Text;
            window.Close();
        };

        var cancel = new Button { Content = "取消", Width = 76, IsCancel = true, Margin = new Thickness(8, 0, 0, 0) };
        cancel.Click += (_, _) => window.Close();

        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);

        window.Content = panel;
        input.SelectAll();
        input.Focus();

        return window.ShowDialog() == true ? result : null;
    }
}
