using MortarHUD.Core.Configuration;
using MortarHUD.Core.Diagnostics;
using MortarHUD.Capture.Ocr;
using OpenCvSharp;

namespace MortarHUD.Capture.Diagnostics;

/// <summary>
/// Debug 转储的实现：只在设置打开对应开关时才写盘（TDD §33）。
/// </summary>
public sealed class CaptureDiagnostics : IRecognitionObserver
{
    private readonly DebugArtifactWriter _writer;
    private readonly Func<DebugSettings> _settingsAccessor;
    private readonly Action<string> _log;

    private string? _baseName;
    private int _remaining;
    private bool _collectThis;
    private string? _rawPath;
    private string? _processedPath;

    public void BeginRequest()
    {
        _baseName = null;
        _rawPath = _processedPath = null;
        _collectThis = false;
    }

    public void CollectNext(int count) => Interlocked.Exchange(ref _remaining, count);

    public CaptureOutcome AttachImages(CaptureOutcome outcome) => outcome with
    { RawImagePath = _rawPath, ProcessedImagePath = _processedPath };

    public CaptureDiagnostics(
        DebugArtifactWriter writer, Func<DebugSettings> settingsAccessor, Action<string> log)
    {
        _writer = writer;
        _settingsAccessor = settingsAccessor;
        _log = log;
    }

    public void OnRawRoi(Mat rawRoi)
    {
        var settings = _settingsAccessor();
        _baseName = DebugArtifactWriter.CreateBaseName(DateTime.Now) + "_" + Guid.NewGuid().ToString("N")[..6];
        _rawPath = _processedPath = null;
        _collectThis = Volatile.Read(ref _remaining) > 0;
        if (_collectThis) Interlocked.Decrement(ref _remaining);

        if (!_collectThis && (!settings.Enabled || !settings.SaveRawRoi))
        {
            return;
        }

        _rawPath = _writer.ResolveRawPath(_baseName);
        TryWrite(_rawPath, rawRoi);
    }

    public void OnProcessed(string engine, string pipeline, Mat processed)
    {
        var settings = _settingsAccessor();

        if (!_collectThis && (!settings.Enabled || !settings.SaveProcessedRoi))
        {
            return;
        }

        var name = _baseName ??= DebugArtifactWriter.CreateBaseName(DateTime.Now);
        var path = Path.Combine(
            _writer.Directory, $"{name}_{engine}_{pipeline}_processed.png");

        TryWrite(path, processed);
        _processedPath = path;
    }

    /// <summary>把这次采集的完整结论写成 JSON，Debug 面板与事后排查都靠它。</summary>
    public void WriteResult(CaptureOutcome outcome, bool isGun)
    {
        var settings = _settingsAccessor();
        if (!settings.Enabled && !_collectThis)
        {
            return;
        }

        var name = _baseName ??= DebugArtifactWriter.CreateBaseName(DateTime.Now);

        var payload = new
        {
            timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"),
            kind = isGun ? "gun" : "target",
            cursor = new { x = outcome.Cursor.X, y = outcome.Cursor.Y },
            roi = new { x = outcome.Roi.X, y = outcome.Roi.Y, w = outcome.Roi.Width, h = outcome.Roi.Height },
            success = outcome.Success,
            error = outcome.Recognition.Error,
            coordinate = outcome.Coordinate is { } c ? new { x = c.X, y = c.Y } : null,
            confidence = outcome.Recognition.Confidence,
            captureMs = outcome.CaptureTime.TotalMilliseconds,
            totalMs = outcome.TotalTime.TotalMilliseconds,
            attempts = outcome.Recognition.Attempts.Select(a => new
            {
                engine = a.Engine,
                pipeline = a.Pipeline,
                success = a.Success,
                error = a.Error,
                rawText = a.RawText,
                repairedText = a.RepairedText,
                confidence = a.Confidence,
                preprocessMs = a.PreprocessTime.TotalMilliseconds,
                ocrMs = a.OcrTime.TotalMilliseconds,
            }),
        };

        try
        {
            _writer.WriteResult(name, payload);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log($"写入 Debug 结果失败：{ex.Message}");
        }
    }

    private static void TryWrite(string path, Mat image)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            Cv2.ImWrite(path, image);
        }
        catch (Exception ex) when (ex is OpenCVException or IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"Debug 截图写入失败 {path}：{ex.Message}");
        }
    }
}