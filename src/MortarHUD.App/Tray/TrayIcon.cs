using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MortarHUD.App.Tray;

/// <summary>
/// 系统托盘图标与菜单（TDD §36）。
/// </summary>
/// <remarks>
/// 用 WinForms 的 <see cref="NotifyIcon"/>：它是框架自带的，
/// 比为此再引入一个托盘库划算。图标也是运行时画出来的，
/// 不往仓库里塞二进制资源。
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

    public TrayIcon()
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
            Visible = true,
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

    /// <summary>运行时画一个准星图标，避免往仓库里放 .ico 二进制。</summary>
    private static Icon CreateCrosshairIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using var graphics = Graphics.FromImage(bitmap);

        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);

        using var pen = new Pen(Color.FromArgb(0x7C, 0xFF, 0x6B), 3f);
        using var darkPen = new Pen(Color.FromArgb(200, 0, 0, 0), 5f);

        // 先描一圈深色底，浅色任务栏上也看得见。
        foreach (var useDark in new[] { true, false })
        {
            var activePen = useDark ? darkPen : pen;

            graphics.DrawEllipse(activePen, 6, 6, 20, 20);
            graphics.DrawLine(activePen, 16, 2, 16, 9);
            graphics.DrawLine(activePen, 16, 23, 16, 30);
            graphics.DrawLine(activePen, 2, 16, 9, 16);
            graphics.DrawLine(activePen, 23, 16, 30, 16);
        }

        var handle = bitmap.GetHicon();

        // GetHicon 拿到的句柄必须克隆成托管 Icon 后立刻销毁，否则会泄漏 GDI 对象。
        using var temporary = Icon.FromHandle(handle);
        var icon = (Icon)temporary.Clone();

        NativeMethods.DestroyIcon(handle);
        return icon;
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

    private static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        internal static extern bool DestroyIcon(IntPtr handle);
    }
}
