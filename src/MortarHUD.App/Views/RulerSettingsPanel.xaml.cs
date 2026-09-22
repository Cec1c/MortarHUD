using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MortarHUD.Core.Configuration;
using MortarHUD.Platform.Windows.Hotkeys;

namespace MortarHUD.App.Views;

public partial class RulerSettingsPanel : UserControl
{
    private readonly ObservableCollection<RulerProfile> _profiles = [];
    public RulerSettingsPanel()
    {
        InitializeComponent();
        ProfilesBox.ItemsSource = _profiles;
        // InitializeComponent 期间控件与集合还没就绪，不在 XAML 上订阅这些事件。
        ProfilesBox.SelectionChanged += (_, _) =>
        {
            ProfileFields.DataContext = ProfilesBox.SelectedItem;
            RefreshPreview();
        };
        ProfileFields.AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler((_, _) => RefreshPreview()));
        RangeBox.TextChanged += (_, _) => RefreshPreview();
    }

    public void Load(RulerSettings settings)
    {
        EnabledCheck.IsChecked = settings.Enabled;
        HotkeyBox.Text = settings.ToggleHotkey;
        _profiles.Clear();
        foreach (var profile in settings.Profiles.Where(p => p.IsValid)) _profiles.Add(profile.Clone());
        if (_profiles.Count == 0) _profiles.Add(new RulerProfile());
        ProfilesBox.SelectedIndex = 0;
    }

    public RulerSettings BuildSettings()
    {
        if (TextFields(ProfileFields).Any(Validation.GetHasError) || _profiles.Any(p => !p.IsValid))
            throw new InvalidOperationException("标尺坐标必须在画面内，50 MIL 间距必须是有效的正数（至少 10 像素）。");
        if (!HotkeyParser.TryParse(HotkeyBox.Text, out var hotkey, out var error))
            throw new InvalidOperationException($"标尺快捷键无效：{error}");
        return new RulerSettings
        {
            Enabled = EnabledCheck.IsChecked == true, ToggleHotkey = hotkey.ToConfigString(),
            Profiles = _profiles.Select(p => p.Clone()).ToList(),
        };
    }

    private static IEnumerable<TextBox> TextFields(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is TextBox box) yield return box;
            foreach (var field in TextFields(child)) yield return field;
        }
    }

    private void RefreshPreview()
    {
        if (ProfilesBox.SelectedItem is not RulerProfile profile) return;
        if (!double.TryParse(RangeBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var range)) return;
        Preview.Update(profile, range, preview: true);
    }

    private void OnAddProfile(object sender, RoutedEventArgs e)
    {
        var size = ResolutionBox.Text.Replace('×', 'x').ToLowerInvariant().Split('x', StringSplitOptions.TrimEntries);
        if (size.Length != 2 || !int.TryParse(size[0], out var width) || !int.TryParse(size[1], out var height)
            || !RulerProfile.CreateDefault(width, height).IsValid)
        {
            Feedback.Text = "请输入游戏分辨率，例如 2560x1440。";
            return;
        }
        var profile = _profiles.FirstOrDefault(p => p.Width == width && p.Height == height);
        if (profile is null) { profile = RulerProfile.CreateDefault(width, height); _profiles.Add(profile); }
        ProfilesBox.SelectedItem = profile;
        Feedback.Text = "已选中该分辨率；点击应用保存校准。";
    }

    private void OnResetProfile(object sender, RoutedEventArgs e)
    {
        if (ProfilesBox.SelectedItem is not RulerProfile profile) return;
        var index = _profiles.IndexOf(profile);
        _profiles[index] = RulerProfile.CreateDefault(profile.Width, profile.Height);
        ProfilesBox.SelectedIndex = index;
        Feedback.Text = "已恢复初值；点击应用保存。";
    }

    public void ScrollToBottomForDiagnostics()
    {
        if (Content is ScrollViewer scroll) scroll.ScrollToEnd();
    }
}
