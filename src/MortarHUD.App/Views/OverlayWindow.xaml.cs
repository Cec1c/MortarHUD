using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using MortarHUD.Core.Configuration;
using MortarHUD.Core.Session;
using MortarHUD.Core.Themes;
using MortarHUD.Platform.Windows.Dpi;
using MortarHUD.Platform.Windows.WindowStyles;

namespace MortarHUD.App.Views;

/// <summary>
/// 贴在游戏之上的 HUD 窗口（TDD §19）。
/// </summary>
/// <remarks>
/// <para>
/// 窗口本体只负责「位置、置顶、穿透」这三件事，文字绘制全部交给
/// <see cref="HudRenderer"/>。
/// </para>
/// <para>
/// 位置用<strong>物理像素 + 显示器工作区</strong>计算，再通过 SetWindowPos 落位，
/// 不走 WPF 的 Left/Top。原因是后两者是 DIP，而 HUD 的锚点必须和游戏在同一套
/// 物理像素坐标系里，否则在多显示器不同缩放的机器上会偏。
/// </para>
/// </remarks>
public partial class OverlayWindow : Window
{
    private IntPtr _handle;
    private HudSettings _settings = new();

    private bool _dragging;
    private System.Drawing.Point _dragOriginScreen;
    private double _dragOriginOffsetX;
    private double _dragOriginOffsetY;

    public OverlayWindow()
    {
        InitializeComponent();

        // 尺寸变化（字号、布局、字段增减）后要重新落位，
        // 否则居中对齐的锚点会停留在旧尺寸算出来的位置上。
        Renderer.SizeChanged += (_, _) => Reposition();
    }

    /// <summary>当前是否处于「可拖动」的编辑状态。</summary>
    public bool IsPositionUnlocked { get; private set; }

    /// <summary>诊断日志出口，由 App 接到文件日志上。</summary>
    /// <remarks>
    /// 位置问题（HUD 跑到哪去了）只能靠记录落位前后的数字来查，
    /// 界面截图看不出「它本该在哪」和「它实际在哪」的差别。
    /// </remarks>
    public Action<string>? Log { get; set; }

    /// <summary>状态提示的透明度，由 <see cref="Services.HudController"/> 驱动淡出。</summary>
    public double StatusOpacity
    {
        get => Renderer.StatusOpacity;
        set => Renderer.StatusOpacity = value;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        _handle = new WindowInteropHelper(this).Handle;

        // 进程是 Per-Monitor V2，这里的样式必须在窗口句柄创建之后设置。
        ApplyOverlayStyles();
        SetTopmost(true);
    }

    /// <summary>应用 HUD 设置（主题、可见性、位置、穿透）。</summary>
    public void ApplySettings(HudSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _settings = settings;
        Renderer.Theme = settings.CurrentTheme;
        IsPositionUnlocked = settings.PositionUnlocked;

        Log?.Invoke($"[HUD] 应用设置：visible={settings.Visible} unlocked={settings.PositionUnlocked} " +
                    $"offset=({settings.OffsetX:0.#},{settings.OffsetY:0.#}) loaded={IsLoaded}");

        ApplyOverlayStyles();
        Reposition();

        if (settings.Visible)
        {
            ShowWithoutActivation();
        }
        else
        {
            Hide();
        }
    }

    /// <summary>更新 HUD 内容。</summary>
    public void UpdateContent(IReadOnlyList<HudLine> lines, string statusText, string statusDetail, MortarStatusKind statusKind)
    {
        Renderer.StatusKind = statusKind;
        Renderer.Update(lines, statusText, statusDetail);

        // 内容变了宽度也会变，居中/右对齐的锚点需要跟着重算。
        Dispatcher.BeginInvoke(Reposition, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>
    /// 显示但不抢焦点（TDD §19.1）。
    /// </summary>
    /// <remarks>
    /// 用 <c>Show()</c> 会让 HUD 抢走游戏的前台状态，全屏游戏会因此最小化。
    /// WS_EX_NOACTIVATE 能挡住大部分情况，但 WPF 自己还会调 SetForegroundWindow，
    /// 所以这里用 SW_SHOWNOACTIVATE 直接走 Win32。
    /// </remarks>
    private void ShowWithoutActivation()
    {
        if (!IsVisible)
        {
            Show();
        }

        if (_handle == IntPtr.Zero)
        {
            return;
        }

        OverlayWindowController.ShowWithoutActivating(_handle);
    }

    private void ApplyOverlayStyles()
    {
        if (_handle == IntPtr.Zero)
        {
            return;
        }

        // 编辑模式下必须关掉穿透，否则鼠标事件全部穿过窗口，根本拖不动。
        OverlayWindowController.ApplyOverlayStyles(_handle, clickThrough: !IsPositionUnlocked);
    }

    private void SetTopmost(bool topmost)
    {
        if (_handle != IntPtr.Zero)
        {
            OverlayWindowController.SetTopmost(_handle, topmost);
        }
    }

    /// <summary>按锚点 + 偏移重新落位（TDD §21.1）。</summary>
    public void Reposition()
    {
        if (_handle == IntPtr.Zero || !IsLoaded)
        {
            // 这条以前是静默返回的。窗口尚未加载时跳过定位是对的（尺寸还没算出来），
            // 但它会让「HUD 停在 WPF 默认位置」和「HUD 落位到设置位置」看起来一样，
            // 排查位置问题时必须先知道到底走没走这一步。
            Log?.Invoke($"[HUD] 落位跳过：handle={(IntPtr.Zero != _handle ? "有" : "无")} loaded={IsLoaded}");
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

        // 落位算法放在 Core，因为那里能写单元测试——「HUD 被推出屏幕后找不回来」
        // 就是漏了那里的边界钳制。见 HudPlacement。
        var (x, y) = HudPlacement.Resolve(workArea, width, height, _settings.Anchor, _settings.OffsetX, _settings.OffsetY, scale);

        Log?.Invoke($"[HUD] 落位：anchor={_settings.Anchor} " +
                    $"offset=({_settings.OffsetX:0.#},{_settings.OffsetY:0.#}) scale={scale:0.##} " +
                    $"area=({workArea.X},{workArea.Y},{workArea.Width}x{workArea.Height}) " +
                    $"size=({width:0.#}x{height:0.#}) -> ({x:0.#},{y:0.#})");

        OverlayWindowController.MoveToPhysical(_handle, (int)Math.Round(x), (int)Math.Round(y));
    }

    // ------------------------------------------------------------ 编辑模式拖动

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);

        if (!IsPositionUnlocked || _handle == IntPtr.Zero)
        {
            return;
        }

        _dragging = true;
        _dragOriginScreen = GetCursorScreenPoint();
        _dragOriginOffsetX = _settings.OffsetX;
        _dragOriginOffsetY = _settings.OffsetY;

        Log?.Invoke($"[HUD] 开始拖动：光标=({_dragOriginScreen.X},{_dragOriginScreen.Y}) " +
                    $"起始偏移=({_dragOriginOffsetX:0.#},{_dragOriginOffsetY:0.#})");

        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (!_dragging)
        {
            return;
        }

        var current = GetCursorScreenPoint();
        var scale = DpiAwareness.GetScaleForWindow(_handle);

        // 拖动量本身是物理像素，换算回设置里的 DIP 偏移量再存。
        _settings.OffsetX = _dragOriginOffsetX + (current.X - _dragOriginScreen.X) / scale;
        _settings.OffsetY = _dragOriginOffsetY + (current.Y - _dragOriginScreen.Y) / scale;

        Reposition();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);

        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        ReleaseMouseCapture();
        e.Handled = true;

        Log?.Invoke($"[HUD] 拖动结束：偏移=({_settings.OffsetX:0.#},{_settings.OffsetY:0.#})");

        PositionChangedByUser?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>用户拖动结束，调用方应把偏移量持久化。</summary>
    public event EventHandler? PositionChangedByUser;

    private static System.Drawing.Point GetCursorScreenPoint()
    {
        var provider = new Platform.Windows.Mouse.WindowsCursorPositionProvider();
        return provider.TryGetCursorPosition(out var x, out var y)
            ? new System.Drawing.Point(x, y)
            : System.Drawing.Point.Empty;
    }
}
