using OpenCvSharp;

namespace MortarHUD.Capture;

/// <summary>截图之后不再读取光标；OCR 只处理这份冻结的画面。</summary>
public sealed class CaptureFrame : IDisposable
{
    public required Mat Image { get; init; }
    public required System.Drawing.Point Cursor { get; init; }
    public required System.Drawing.Rectangle Roi { get; init; }
    public TimeSpan CaptureTime { get; init; }
    public void Dispose() => Image.Dispose();
}
