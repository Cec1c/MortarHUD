using System.Drawing;
using System.Windows.Forms;

namespace MortarHUD.App.Tray;

/// <summary>
/// 系统托盘图标与菜单（TDD §36）。
/// </summary>
/// <remarks>
/// 用 WinForms 的 <see cref="NotifyIcon"/>：它是框架自带的，
/// 图标与 EXE 共用内嵌资源，避免小尺寸图标的视觉不一致。
/// </remarks>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly Icon _icon;
    private readonly ToolStripMenuItem _toggleHudItem;
    private readonly ToolStripMenuItem _debugItem;
    private readonly ToolStripMenuItem _captureGunItem;
    private readonly ToolStripMenuItem _captureTargetItem;

    private bool _disposed;

    public TrayIcon(bool visible = true)
    {
        _icon = CreateCrosshairIcon();

        _toggleHudItem = new ToolStripMenuItem("显示 HUD", null, (_, _) => ToggleHudRequested?.Invoke(this, EventArgs.Empty))
        {
            CheckOnClick = false,
        };

        _debugItem = new ToolStripMenuItem("Debug 模式", null, (_, _) => ToggleDebugRequested?.Invoke(this, EventArgs.Empty))
        {
            CheckOnClick = false,
        };

        _captureGunItem = new ToolStripMenuItem("记录炮位", null, (_, _) => CaptureGunRequested?.Invoke(this, EventArgs.Empty));
        _captureTargetItem = new ToolStripMenuItem("记录目标", null, (_, _) => CaptureTargetRequested?.Invoke(this, EventArgs.Empty));

        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("打开设置", null, (_, _) => ShowSettingsRequested?.Invoke(this, EventArgs.Empty)));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_toggleHudItem);
        menu.Items.Add(_captureGunItem);
        menu.Items.Add(_captureTargetItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_debugItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("退出", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty)));

        _notifyIcon = new NotifyIcon
        {
            Icon = _icon,
            Text = "MortarHUD —— 迫击炮坐标解算",
            Visible = visible,
            ContextMenuStrip = menu,
        };

        // 双击托盘图标是最直觉的「打开设置」入口。
        _notifyIcon.DoubleClick += (_, _) => ShowSettingsRequested?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? ShowSettingsRequested;
    public event EventHandler? ToggleHudRequested;
    public event EventHandler? ToggleDebugRequested;
    public event EventHandler? CaptureGunRequested;
    public event EventHandler? CaptureTargetRequested;
    public event EventHandler? ExitRequested;

    /// <summary>
    /// 按实际绑定刷新菜单上的键位提示。
    /// </summary>
    /// <remarks>
    /// 写死 "(F6)" 之类的文案会在用户改键之后变成错误信息，
    /// 所以这里由调用方把当前键位的显示名传进来。
    /// </remarks>
    public void UpdateHotkeys(string captureGun, string captureTarget)
    {
        if (_disposed)
        {
            return;
        }

        _captureGunItem.Text = string.IsNullOrEmpty(captureGun) ? "记录炮位" : $"记录炮位（{captureGun}）";
        _captureTargetItem.Text = string.IsNullOrEmpty(captureTarget) ? "记录目标" : $"记录目标（{captureTarget}）";
    }

    public void UpdateState(bool hudVisible, bool debugEnabled, bool positionUnlocked)
    {
        _toggleHudItem.Checked = hudVisible;
        _toggleHudItem.Text = positionUnlocked ? "锁定 HUD 位置" : "显示 HUD";
        _debugItem.Checked = debugEnabled;
    }

    /// <summary>弹一条气泡提示，用于热键注册失败之类必须让用户知道的事件。</summary>
    public void ShowBalloon(string title, string message, bool isError = false)
    {
        if (_disposed)
        {
            return;
        }

        _notifyIcon.BalloonTipTitle = title;
        _notifyIcon.BalloonTipText = message;
        _notifyIcon.BalloonTipIcon = isError ? ToolTipIcon.Error : ToolTipIcon.Info;
        _notifyIcon.ShowBalloonTip(6000);
    }

    /// <summary>与 EXE 和窗口共用图标，不再分开维护运行时绘图。</summary>
    private static Icon CreateCrosshairIcon()
    {
        var resource = System.Windows.Application.GetResourceStream(
            new Uri("pack://application:,,,/Assets/MortarHUD.ico"));
        using var stream = resource.Stream;
        using var icon = new Icon(stream, 32, 32);
        return (Icon)icon.Clone();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _icon.Dispose();
        GC.SuppressFinalize(this);
    }

}
