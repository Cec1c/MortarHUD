using System.Diagnostics;
using MortarHUD.Localization;
using MortarHUD.Capture.Diagnostics;
using MortarHUD.Capture.Ocr;
using MortarHUD.Capture.ScreenCapture;
using MortarHUD.Core.Configuration;
using MortarHUD.Core.Models;
using MortarHUD.Platform.Windows.Mouse;
using MortarHUD.Platform.Windows.WindowStyles;
using OpenCvSharp;

namespace MortarHUD.Capture;

/// <summary>一次采集的完整结果，包含识别结论与 Debug 需要的全部中间量。</summary>
public sealed record CaptureOutcome
{
    public required RecognitionOutcome Recognition { get; init; }

    /// <summary>光标物理像素位置。</summary>
    public System.Drawing.Point Cursor { get; init; }

    /// <summary>实际截取的 ROI（已裁进屏幕范围）。</summary>
    public System.Drawing.Rectangle Roi { get; init; }

    public TimeSpan CaptureTime { get; init; }

    public TimeSpan TotalTime { get; init; }
    public string? RawImagePath { get; init; }
    public string? ProcessedImagePath { get; init; }

    public bool Success => Recognition.Success;

    public MapCoordinate? Coordinate => Recognition.Coordinate;

    public static CaptureOutcome Failed(string error, TimeSpan total) => new()
    {
        Recognition = new RecognitionOutcome { Success = false, Error = error },
        TotalTime = total,
    };
}

/// <summary>
/// 把「按下热键」到「拿到坐标」这条链路串起来（TDD §10.1）。
/// </summary>
/// <remarks>
/// <para>
/// 顺序是固定的：
/// <code>
/// GetCursorPos → 鼠标相对 ROI → 截图 → 预处理 → OCR → 解析 → 校验
/// </code>
/// </para>
/// <para>
/// 鼠标在这里<strong>不是</strong>测量工具，它只回答一个问题：
/// 游戏此刻把 x/y 读数画在了屏幕的哪个位置。
/// 因为读的是游戏自己显示的绝对坐标，地图缩放、平移、重新居中都不影响结果。
/// </para>
/// </remarks>
public sealed class MortarCaptureService : IDisposable
{
    private readonly IScreenCaptureProvider _captureProvider;
    private readonly ICursorPositionProvider _cursorProvider;
    private readonly CoordinateRecognizer _recognizer;
    private readonly Func<RoiSettings> _roiSettingsAccessor;
    private readonly Action<string>? _log;
    private readonly Func<DebugSettings>? _debugSettingsAccessor;
    private readonly string? _sampleDirectory;

    /// <summary>
    /// 采集闸门：同一时刻只允许一次采集。
    /// </summary>
    /// <remarks>
    /// 必须用 <see cref="SemaphoreSlim"/> 而不是 <c>lock</c> / <c>Monitor</c>：
    /// 下面这段逻辑中间有 <c>await</c>，而 await 之后的续体可能落在另一条线程上。
    /// <c>Monitor.Exit</c> 要求「由进入的那个线程调用」，跨 await 用必然抛
    /// SynchronizationLockException——热键通道会整个失效。
    /// </remarks>
    private readonly SemaphoreSlim _gate = new(1, 1);

    private bool _disposed;
    private SampleFrameWriter? _sampleWriter;

    public MortarCaptureService(
        IScreenCaptureProvider captureProvider,
        ICursorPositionProvider cursorProvider,
        CoordinateRecognizer recognizer,
        Func<RoiSettings> roiSettingsAccessor,
        Action<string>? log = null,
        Func<DebugSettings>? debugSettingsAccessor = null,
        string? sampleDirectory = null)
    {
        _captureProvider = captureProvider;
        _cursorProvider = cursorProvider;
        _recognizer = recognizer;
        _roiSettingsAccessor = roiSettingsAccessor;
        _log = log;
        _debugSettingsAccessor = debugSettingsAccessor;
        _sampleDirectory = sampleDirectory;
    }

    public async Task<CaptureOutcome> CaptureAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var totalWatch = Stopwatch.StartNew();

        // 一次只跑一个采集：热键连按时直接忽略，而不是排队堆起来。
        if (!await _gate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            _log?.Invoke("上一次采集还没结束，忽略本次触发。");
            return CaptureOutcome.Failed("BUSY", totalWatch.Elapsed);
        }

        try
        {
            using var frame = CaptureFrame(cancellationToken);
            return await RecognizeFrameAsync(frame, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _log?.Invoke($"采集失败：{ex}");
            return CaptureOutcome.Failed($"CAPTURE_FAILED: {ex.Message}", totalWatch.Elapsed);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 取光标所在显示器的物理高度，作为 ROI 自动缩放的基准。
    /// </summary>
    /// <remarks>
    /// 拿不到单显示器信息时退回虚拟桌面高度——单显示器情况下两者相同。
    /// </remarks>
    private static int ResolveMonitorHeight(int cursorX, int cursorY, int virtualScreenHeight)
    {
        var monitor = OverlayWindowController.GetMonitorBoundsForPoint(cursorX, cursorY);

        if (monitor.Height > 0)
        {
            return monitor.Height;
        }

        return virtualScreenHeight > 0 ? virtualScreenHeight : 1080;
    }

    /// <summary>只截取画面，不等待 OCR 或 PNG 编码；允许 M 在短窗口内保存连续帧。</summary>
    public CaptureFrame CaptureFrame(CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        token.ThrowIfCancellationRequested();
        if (!_cursorProvider.TryGetCursorPosition(out var x, out var y))
            throw new ScreenCaptureException("无法读取光标位置");
        var bounds = OverlayWindowController.GetVirtualScreenBounds();
        var rect = RoiCalculator.Compute(x, y, _roiSettingsAccessor(), ResolveMonitorHeight(x, y, bounds.Height), bounds);
        var watch = Stopwatch.StartNew();
        var capturedAt = DateTime.Now;
        Mat image;
        if (_debugSettingsAccessor?.Invoke().SaveFullFrame == true)
        {
            // ROI 必须来自同一份画面。旧实现先编码整屏再另截 ROI，样本清晰而 OCR 已被浮层遮住。
            var full = _captureProvider.Capture(bounds);
            try
            {
                using var region = new Mat(full, new Rect(rect.X - bounds.X, rect.Y - bounds.Y, rect.Width, rect.Height));
                image = region.Clone();
            }
            catch { full.Dispose(); throw; }
            (_sampleWriter ??= new SampleFrameWriter(_log, _sampleDirectory)).Enqueue(full, bounds, new(x, y), capturedAt);
        }
        else image = _captureProvider.Capture(rect);
        return new CaptureFrame { Image = image, Cursor = new(x, y), Roi = rect, CaptureTime = watch.Elapsed };
    }

    public async Task<CaptureOutcome> RecognizeFrameAsync(CaptureFrame frame, CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var watch = Stopwatch.StartNew();
        var cursorInRoi = new System.Drawing.Point(frame.Cursor.X - frame.Roi.X, frame.Cursor.Y - frame.Roi.Y);
        var result = await _recognizer.RecognizeAsync(frame.Image, token, cursorInRoi).ConfigureAwait(false);
        return new CaptureOutcome
        {
            Recognition = result, Cursor = frame.Cursor, Roi = frame.Roi,
            CaptureTime = frame.CaptureTime, TotalTime = frame.CaptureTime + watch.Elapsed,
        };
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _sampleWriter?.Dispose();
        _captureProvider.Dispose();
        _gate.Dispose();
        GC.SuppressFinalize(this);
    }
}
