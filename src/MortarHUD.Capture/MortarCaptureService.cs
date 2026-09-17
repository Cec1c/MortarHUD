using System.Diagnostics;
using MortarHUD.Localization;
using System.Text.Json;
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

    public MortarCaptureService(
        IScreenCaptureProvider captureProvider,
        ICursorPositionProvider cursorProvider,
        CoordinateRecognizer recognizer,
        Func<RoiSettings> roiSettingsAccessor,
        Action<string>? log = null,
        Func<DebugSettings>? debugSettingsAccessor = null)
    {
        _captureProvider = captureProvider;
        _cursorProvider = cursorProvider;
        _recognizer = recognizer;
        _roiSettingsAccessor = roiSettingsAccessor;
        _log = log;
        _debugSettingsAccessor = debugSettingsAccessor;
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
            if (!_cursorProvider.TryGetCursorPosition(out var cursorX, out var cursorY))
            {
                return CaptureOutcome.Failed(Loc.T("CAPTUREFAILEDCannotReadTheCursorPosition"), totalWatch.Elapsed);
            }

            var roi = _roiSettingsAccessor();
            var virtualScreen = OverlayWindowController.GetVirtualScreenBounds();

            // 缩放基准取「光标所在那块显示器的物理高度」，不能取虚拟桌面总高度
            // （上下拼接多屏时会大得离谱），更不能除以 DPI 缩放系数
            // （游戏 UI 是按渲染分辨率画的，和 Windows 的缩放比例无关）。
            var screenHeight = ResolveMonitorHeight(cursorX, cursorY, virtualScreen.Height);

            var rect = RoiCalculator.Compute(cursorX, cursorY, roi, screenHeight, virtualScreen);

            SaveSampleFrameIfEnabled(virtualScreen, cursorX, cursorY);

            var captureWatch = Stopwatch.StartNew();
            Mat image;

            try
            {
                image = _captureProvider.Capture(rect);
            }
            catch (ScreenCaptureException ex)
            {
                _log?.Invoke($"截图失败：{ex.Message}");
                return CaptureOutcome.Failed($"CAPTURE_FAILED: {ex.Message}", totalWatch.Elapsed);
            }

            captureWatch.Stop();

            try
            {
                using (image)
                {
                    // 把光标在 ROI 内的位置交给识别器：它要在预处理前把光标锚点
                    // （游戏画的箭头和括号）抹掉，否则会和 x 行混成一行。
                    var cursorInRoi = new System.Drawing.Point(cursorX - rect.X, cursorY - rect.Y);

                    var recognition = await _recognizer
                        .RecognizeAsync(image, cancellationToken, cursorInRoi)
                        .ConfigureAwait(false);

                    totalWatch.Stop();

                    return new CaptureOutcome
                    {
                        Recognition = recognition,
                        Cursor = new System.Drawing.Point(cursorX, cursorY),
                        Roi = rect,
                        CaptureTime = captureWatch.Elapsed,
                        TotalTime = totalWatch.Elapsed,
                    };
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log?.Invoke($"识别失败：{ex}");
                return CaptureOutcome.Failed($"OCR_FAILED: {ex.Message}", totalWatch.Elapsed);
            }
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

    /// <summary>
    /// 采样模式下额外存一张整屏 + 当时的光标位置。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 存整屏而不是只存 ROI，是因为要标定「坐标文字在光标的哪个方位」时，
    /// ROI 本身给不出参照——只有整屏加上光标位置这一对，才能事后把文字量准。
    /// </para>
    /// <para>
    /// 用 <c>ImEncode</c> 而不是 <c>ImWrite</c>：后者对非 ASCII 路径会静默失败，
    /// 而 <c>%AppData%</c> 在中文用户名下就是非 ASCII 的。
    /// </para>
    /// </remarks>
    private void SaveSampleFrameIfEnabled(System.Drawing.Rectangle virtualScreen, int cursorX, int cursorY)
    {
        if (_debugSettingsAccessor?.Invoke().SaveFullFrame != true)
        {
            return;
        }

        try
        {
            var stamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss_fff");
            Directory.CreateDirectory(AppPaths.SamplesDirectory);

            using var frame = _captureProvider.Capture(virtualScreen);
            Cv2.ImEncode(".png", frame, out var encoded);
            File.WriteAllBytes(Path.Combine(AppPaths.SamplesDirectory, $"{stamp}_frame.png"), encoded);

            var payload = JsonSerializer.Serialize(new
            {
                timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                cursor = new { x = cursorX, y = cursorY },
                screen = new
                {
                    x = virtualScreen.X,
                    y = virtualScreen.Y,
                    w = virtualScreen.Width,
                    h = virtualScreen.Height,
                },
            }, new JsonSerializerOptions { WriteIndented = true });

            File.WriteAllText(Path.Combine(AppPaths.SamplesDirectory, $"{stamp}_sample.json"), payload);
        }
        catch (Exception ex) when (ex is OpenCVException or IOException or UnauthorizedAccessException or ScreenCaptureException)
        {
            _log?.Invoke($"采样保存失败：{ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _captureProvider.Dispose();
        _gate.Dispose();
        GC.SuppressFinalize(this);
    }
}
