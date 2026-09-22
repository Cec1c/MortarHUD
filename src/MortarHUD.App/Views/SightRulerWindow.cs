using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using MortarHUD.Core.Configuration;
using MortarHUD.Platform.Windows.WindowStyles;

namespace MortarHUD.App.Views;

public sealed class SightRulerWindow : Window
{
    private IntPtr _handle;
    private System.Drawing.Rectangle _bounds;
    public SightRulerRenderer Renderer { get; } = new();

    public SightRulerWindow()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        Topmost = true;
        IsHitTestVisible = false;
        Content = Renderer;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _handle = new WindowInteropHelper(this).Handle;
        OverlayWindowController.ApplyOverlayStyles(_handle, true);
    }

    public void Display(System.Drawing.Rectangle bounds, RulerProfile profile, double? range, bool estimated)
    {
        if (!IsVisible) Show();
        if (_bounds != bounds)
        {
            _bounds = bounds;
            OverlayWindowController.PlaceInClientBounds(_handle, bounds);
        }
        Renderer.Update(profile, range, note: estimated ? "分辨率初值 · 可在设置中校准" : "");
        OverlayWindowController.ShowWithoutActivating(_handle);
    }
}
