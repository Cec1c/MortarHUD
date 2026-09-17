using System.Windows.Threading;
using MortarHUD.App.Views;
using MortarHUD.Core.Configuration;
using MortarHUD.Core.Models;
using MortarHUD.Core.Session;

namespace MortarHUD.App.Services;

/// <summary>
/// 把 <see cref="MortarSession"/> 的状态翻译成 HUD 上显示的内容，并驱动状态提示淡出。
/// </summary>
/// <remarks>
/// 单独拎出来是为了让「什么时候显示什么」这件事不散落在 App 的事件处理里——
/// TDD §20 要求状态信息自动淡出、核心 AZ/RNG 持续显示，这个时间行为需要一处集中管理。
/// </remarks>
public sealed class HudController : IDisposable
{
    /// <summary>状态提示完整显示的时长，之后开始淡出。</summary>
    private static readonly TimeSpan StatusHoldDuration = TimeSpan.FromMilliseconds(1600);

    /// <summary>淡出总时长。</summary>
    private static readonly TimeSpan StatusFadeDuration = TimeSpan.FromMilliseconds(600);

    private readonly MortarSession _session;
    private readonly OverlayWindow _overlay;
    private readonly Func<HudSettings> _settingsAccessor;
    private readonly DispatcherTimer _statusTimer;

    private DateTime _statusShownAt;
    private bool _disposed;

    public HudController(MortarSession session, OverlayWindow overlay, Func<HudSettings> settingsAccessor)
    {
        _session = session;
        _overlay = overlay;
        _settingsAccessor = settingsAccessor;

        _statusTimer = new DispatcherTimer(DispatcherPriority.Background, overlay.Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(50),
        };

        _statusTimer.Tick += OnStatusTick;
    }

    /// <summary>状态提示当前的透明度，0 表示已经淡没。</summary>
    public double StatusOpacity { get; private set; } = 1.0;

    private void SetStatusOpacity(double value)
    {
        StatusOpacity = Math.Clamp(value, 0.0, 1.0);
        _overlay.StatusOpacity = StatusOpacity;
    }

    public void ApplySettings(HudSettings settings)
    {
        _overlay.ApplySettings(settings);
        Refresh();
    }

    /// <summary>重新把当前状态画到 HUD 上。</summary>
    public void Refresh()
    {
        var settings = CurrentSettings;
        var lines = HudLayoutFormatter.BuildLines(_session, settings);

        var statusText = HudLayoutFormatter.DescribeStatus(_session.LastStatus);
        var statusDetail = HudLayoutFormatter.DescribeStatusDetail(_session.LastStatus) ?? "";

        if (string.IsNullOrEmpty(statusText))
        {
            SetStatusOpacity(0.0);
        }
        else
        {
            _overlay.StatusOpacity = StatusOpacity;
        }

        _overlay.UpdateContent(lines, statusText, statusDetail, _session.LastStatus);
        _overlay.Opacity = settings.CurrentTheme.Opacity;
    }

    /// <summary>
    /// 记录一次操作产生的新状态，并重启淡出计时。
    /// </summary>
    public void NotifyStatusChanged()
    {
        SetStatusOpacity(1.0);
        _statusShownAt = DateTime.UtcNow;

        Refresh();

        if (_session.LastStatus != MortarStatusKind.None && !_statusTimer.IsEnabled)
        {
            _statusTimer.Start();
        }
    }

    private void OnStatusTick(object? sender, EventArgs e)
    {
        var elapsed = DateTime.UtcNow - _statusShownAt;

        if (elapsed < StatusHoldDuration)
        {
            SetStatusOpacity(1.0);
            return;
        }

        var fadeProgress = (elapsed - StatusHoldDuration).TotalMilliseconds / StatusFadeDuration.TotalMilliseconds;
        SetStatusOpacity(1.0 - fadeProgress);

        if (StatusOpacity > 0.0)
        {
            return;
        }

        // 淡完了就把状态清掉，核心的 AZ / RNG 继续显示（TDD §20）。
        _statusTimer.Stop();
        _session.ClearStatus();
        Refresh();
    }

    private HudSettings CurrentSettings => _settingsAccessor();

    /// <summary>最近一次解算结果，用于设置页的实时预览。</summary>
    public MortarSolution Solution => _session.Solution;

    public MapCoordinate? Gun => _session.Gun;

    public MapCoordinate? Target => _session.Target;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _statusTimer.Stop();
        _statusTimer.Tick -= OnStatusTick;
        GC.SuppressFinalize(this);
    }
}
