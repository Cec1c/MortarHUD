using System.Text.Json;
using System.Threading.Channels;
using MortarHUD.Core.Configuration;
using OpenCvSharp;

namespace MortarHUD.Capture.Diagnostics;

/// <summary>整屏 PNG 编码不能占用开图后短暂的可读窗口，队列满时放弃诊断帧。</summary>
internal sealed class SampleFrameWriter : IDisposable
{
    private sealed record Sample(Mat Frame, System.Drawing.Rectangle Screen, System.Drawing.Point Cursor, DateTime CapturedAt);
    private readonly Channel<Sample> _queue = Channel.CreateBounded<Sample>(2);
    private readonly Task _worker;

    public SampleFrameWriter(Action<string>? log, string? directory = null)
    {
        directory ??= AppPaths.SamplesDirectory;
        _worker = Task.Run(async () =>
        {
            await foreach (var sample in _queue.Reader.ReadAllAsync())
            {
                using (sample.Frame)
                {
                    try
                    {
                        Directory.CreateDirectory(directory);
                        var stem = sample.CapturedAt.ToString("yyyy-MM-dd_HHmmss_fff");
                        Cv2.ImEncode(".png", sample.Frame, out var bytes);
                        File.WriteAllBytes(Path.Combine(directory, stem + "_frame.png"), bytes);
                        var payload = new
                        {
                            timestamp = sample.CapturedAt.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                            cursor = new { x = sample.Cursor.X, y = sample.Cursor.Y },
                            screen = new { x = sample.Screen.X, y = sample.Screen.Y, w = sample.Screen.Width, h = sample.Screen.Height },
                            roiFromSameFrame = true,
                        };
                        File.WriteAllText(Path.Combine(directory, stem + "_sample.json"),
                            JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
                    }
                    catch (Exception ex) { log?.Invoke($"采样保存失败：{ex.Message}"); }
                }
            }
        });
    }

    public void Enqueue(Mat frame, System.Drawing.Rectangle screen, System.Drawing.Point cursor, DateTime capturedAt)
    {
        if (!_queue.Writer.TryWrite(new(frame, screen, cursor, capturedAt))) frame.Dispose();
    }

    public void Dispose()
    {
        _queue.Writer.TryComplete();
        _worker.GetAwaiter().GetResult();
    }
}
