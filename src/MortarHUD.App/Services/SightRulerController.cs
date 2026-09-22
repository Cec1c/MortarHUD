using System.Windows.Threading;
using MortarHUD.App.Views;
using MortarHUD.Core.Configuration;
using MortarHUD.Core.Session;
using MortarHUD.Platform.Windows.Mouse;

namespace MortarHUD.App.Services;

/// <summary>只跟随最近采集的窗口几何信息；刷新标尺不会截屏或读取游戏状态。</summary>
public sealed class SightRulerController : IDisposable
{
    private readonly SightRulerWindow _window = new();
    private readonly Func<MortarHudSettings> _settings;
    private readonly Func<MortarSession?> _session;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private IntPtr _gameWindow;
    private bool _visible = true;

    public SightRulerController(Func<MortarHudSettings> settings, Func<MortarSession?> session)
    {
        _settings = settings;
        _session = session;
        _timer.Tick += OnTick;
        _timer.Start();
    }

    public void Follow(IntPtr window)
    {
        if (CaptureContext.IsExternal(window)) _gameWindow = window;
    }

    public void Toggle()
    {
        _visible = !_visible;
        Refresh();
    }

    private void OnTick(object? sender, EventArgs e) => Refresh();

    public void Refresh()
    {
        var settings = _settings();
        if (!settings.Ruler.Enabled || !settings.Hud.Visible || !_visible || _gameWindow == IntPtr.Zero
            || CaptureContext.Foreground != _gameWindow || !CaptureContext.TryGetClientBounds(_gameWindow, out var bounds))
        {
            _window.Hide();
            return;
        }
        var profile = settings.Ruler.Resolve(bounds.Width, bounds.Height);
        if (!profile.IsValid) { _window.Hide(); return; }
        var session = _session();
        var range = session?.Gun is not null && session.Target is not null ? (double?)session.Solution.RangeMeters : null;
        var estimated = !settings.Ruler.Profiles.Any(p => p.Width == bounds.Width && p.Height == bounds.Height && p.IsValid);
        _window.Display(bounds, profile, range, estimated);
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= OnTick;
        _window.Close();
    }
}
