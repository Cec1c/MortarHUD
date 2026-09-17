using System.Windows;
using System.Windows.Interop;
using MortarHUD.Core.Configuration;
using MortarHUD.Core.Session;
using MortarHUD.Platform.Windows.Dpi;
using MortarHUD.Platform.Windows.WindowStyles;

namespace MortarHUD.App.Views;

/// <summary>
/// Debug 信息面板（TDD §34）。
/// </summary>
/// <remarks>
/// <para>
/// 与主 HUD 是两个独立窗口，视觉上也刻意做出区分：半透明底板 + 更小的字号，
/// 一眼就能看出「这是调试信息，不是射击数据」。
/// </para>
/// <para>
/// 位置固定在主 HUD 的对角（主 HUD 在左，它就在右下），避免互相压住。
/// </para>
/// </remarks>
public partial class DebugOverlayWindow : Window
{
    private IntPtr _handle;
    private HudSettings _hudSettings = new();

    public DebugOverlayWindow()
    {
        InitializeComponent();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        _handle = new WindowInteropHelper(this).Handle;

        // Debug 面板永远保持鼠标穿透：它只是个观察窗口，
        // 不该影响游戏操作，也不提供任何可交互元素。
        OverlayWindowController.ApplyOverlayStyles(_handle, clickThrough: true);
        OverlayWindowController.SetTopmost(_handle, topmost: true);
    }

    /// <summary>应用可见性与主题。</summary>
    public void ApplySettings(HudSettings hudSettings, DebugSettings debugSettings)
    {
        ArgumentNullException.ThrowIfNull(hudSettings);
        ArgumentNullException.ThrowIfNull(debugSettings);

        _hudSettings = hudSettings;
        Renderer.Theme = DebugInfoFormatter.BuildDebugTheme(hudSettings.CurrentTheme);

        if (debugSettings.Enabled && hudSettings.Visible)
        {
            ShowWithoutActivation();
            Reposition();
        }
        else
        {
            Hide();
        }
    }

    /// <summary>刷新面板内容。</summary>
    public void UpdateContent(DebugSnapshot snapshot, DebugSettings debugSettings)
    {
        Renderer.Update(DebugInfoFormatter.BuildLines(snapshot, debugSettings), "", "");
        Dispatcher.BeginInvoke(Reposition, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void ShowWithoutActivation()
    {
        if (!IsVisible)
        {
            Show();
        }

        if (_handle != IntPtr.Zero)
        {
            OverlayWindowController.ShowWithoutActivating(_handle);
        }
    }

    /// <summary>贴在工作区的右下角，与默认放在左侧的主 HUD 错开。</summary>
    private void Reposition()
    {
        if (_handle == IntPtr.Zero || !IsLoaded)
        {
            return;
        }

        var workArea = OverlayWindowController.GetMonitorWorkArea(_handle);
        if (workArea.Width <= 0 || workArea.Height <= 0)
        {
            return;
        }

        var scale = DpiAwareness.GetScaleForWindow(_handle);
        var width = ActualWidth * scale;
        var height = ActualHeight * scale;

        var x = workArea.Right - width - 24 * scale;
        var y = workArea.Bottom - height - 24 * scale;

        OverlayWindowController.MoveToPhysical(_handle, (int)Math.Round(x), (int)Math.Round(y));
    }

    /// <summary>供 App 判断是否需要显示（主 HUD 隐藏时 Debug 面板也隐藏）。</summary>
    public bool ShouldShow(DebugSettings debugSettings) => debugSettings.Enabled && _hudSettings.Visible;
}
