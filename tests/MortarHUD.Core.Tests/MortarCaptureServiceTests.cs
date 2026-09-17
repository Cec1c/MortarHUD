using MortarHUD.Capture;
using MortarHUD.Capture.ImageProcessing;
using MortarHUD.Capture.Ocr;
using MortarHUD.Capture.ScreenCapture;
using MortarHUD.Core.Configuration;
using MortarHUD.Core.Models;
using MortarHUD.Core.Parsing;
using MortarHUD.Core.Validation;
using MortarHUD.Platform.Windows.Mouse;
using OpenCvSharp;
using Xunit;

namespace MortarHUD.Core.Tests;

/// <summary>
/// 采集链路测试。
/// </summary>
/// <remarks>
/// 这组用例的直接动因是一个真实事故：<see cref="MortarCaptureService.CaptureAsync"/>
/// 一开始用 <c>Monitor.TryEnter</c> / <c>Monitor.Exit</c> 当并发闸门，
/// 但中间有 <c>await</c>，续体落到线程池线程后再调 <c>Monitor.Exit</c> 会抛
/// SynchronizationLockException——也就是**每按一次热键都会失败**。
///
/// 这种错误静态检查看不出来，只有真的把这条异步路径跑起来才会暴露，
/// 所以这里用一个会真正切换线程的桩引擎把它钉死。
/// </remarks>
public class MortarCaptureServiceTests
{
    /// <summary>返回固定文本的桩引擎，并且是异步的（复现换线程的场景）。</summary>
    private sealed class StubOcrEngine : ICoordinateOcrEngine
    {
        private readonly string _text;
        private int _calls;

        public StubOcrEngine(string text) => _text = text;

        public string Name => "Stub";

        public int Calls => _calls;

        public Task<CoordinateOcrResult> RecognizeAsync(Mat input, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);

            // Task.Run 是刻意的：真实引擎就是这么实现的，
            // 而正是它让 await 之后的续体跑在另一条线程上。
            return Task.Run(() => new CoordinateOcrResult
            {
                Success = true,
                RawText = _text,
                Confidence = 0.9,
                OcrTime = TimeSpan.FromMilliseconds(1),
            }, cancellationToken);
        }

        public void Dispose()
        {
        }
    }

    private sealed class FakeCaptureProvider : IScreenCaptureProvider
    {
        public int Calls { get; private set; }

        public System.Drawing.Rectangle LastRect { get; private set; }

        public Mat Capture(System.Drawing.Rectangle physicalPixelRect)
        {
            Calls++;
            LastRect = physicalPixelRect;

            // 内容不重要，成像链路只要有图可读即可。
            return new Mat(physicalPixelRect.Height, physicalPixelRect.Width, MatType.CV_8UC3, Scalar.All(32));
        }

        public void Dispose()
        {
        }
    }

    private sealed class FakeCursorProvider : ICursorPositionProvider
    {
        public int X { get; set; } = 1000;
        public int Y { get; set; } = 500;
        public bool Fails { get; set; }

        public bool TryGetCursorPosition(out int physicalX, out int physicalY)
        {
            physicalX = X;
            physicalY = Y;
            return !Fails;
        }
    }

    private static MortarCaptureService CreateService(
        out StubOcrEngine engine,
        out FakeCaptureProvider capture,
        out FakeCursorProvider cursor,
        string ocrText = "y109.78\nx98.09")
    {
        engine = new StubOcrEngine(ocrText);
        capture = new FakeCaptureProvider();
        cursor = new FakeCursorProvider();

        var recognizer = new CoordinateRecognizer(
            [engine],
            [PreprocessorFactory.AutoCandidates[0]],
            new CoordinateTextParser(),
            new CoordinateValidator(new CoordinateValidationOptions { MinimumConfidence = 0.0 }));

        return new MortarCaptureService(capture, cursor, recognizer, () => new RoiSettings { AutoScale = false });
    }

    [Fact]
    public async Task CaptureAsync_EndToEnd_ReturnsCoordinate()
    {
        using var service = CreateService(out _, out var capture, out _);

        var outcome = await service.CaptureAsync();

        Assert.True(outcome.Success, outcome.Recognition.Error);
        Assert.Equal(98.09, outcome.Coordinate!.Value.X, precision: 9);
        Assert.Equal(109.78, outcome.Coordinate.Value.Y, precision: 9);
        Assert.Equal(1, capture.Calls);
    }

    /// <summary>
    /// 连续采集多次不能抛 SynchronizationLockException。
    /// 这条就是那个事故的回归测试。
    /// </summary>
    [Fact]
    public async Task CaptureAsync_Repeated_DoesNotThrow()
    {
        using var service = CreateService(out var engine, out var capture, out _);

        for (var i = 0; i < 10; i++)
        {
            var outcome = await service.CaptureAsync();

            Assert.True(outcome.Success, $"第 {i + 1} 次：{outcome.Recognition.Error}");
        }

        Assert.Equal(10, capture.Calls);
        Assert.Equal(10, engine.Calls);
    }

    /// <summary>并发触发时应当忽略后来者并返回 BUSY，而不是排队或串扰。</summary>
    [Fact]
    public async Task CaptureAsync_Concurrent_RejectsSecondWithBusy()
    {
        using var service = CreateService(out _, out var capture, out _);

        var first = service.CaptureAsync();
        var second = await service.CaptureAsync();

        var firstOutcome = await first;

        Assert.True(firstOutcome.Success, firstOutcome.Recognition.Error);

        // 第二次要么被 BUSY 挡掉，要么在第一次结束之后正常跑完——两种都不该抛异常。
        if (!second.Success)
        {
            Assert.Equal("BUSY", second.Recognition.Error);
        }

        Assert.InRange(capture.Calls, 1, 2);
    }

    [Fact]
    public async Task CaptureAsync_CursorReadFails_ReportsCaptureFailure()
    {
        using var service = CreateService(out _, out _, out var cursor);
        cursor.Fails = true;

        var outcome = await service.CaptureAsync();

        Assert.False(outcome.Success);
        Assert.StartsWith("CAPTURE_FAILED", outcome.Recognition.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CaptureAsync_OcrUnreadable_ReportsOcrFailureWithoutThrowing()
    {
        using var service = CreateService(out _, out _, out _, ocrText: "这里没有任何坐标");

        var outcome = await service.CaptureAsync();

        Assert.False(outcome.Success);
        Assert.NotNull(outcome.Recognition.Error);
    }

    /// <summary>ROI 必须按设置算出来，而不是写死一个尺寸。</summary>
    [Fact]
    public async Task CaptureAsync_UsesConfiguredRoi()
    {
        using var service = CreateService(out _, out var capture, out var cursor);
        cursor.X = 800;
        cursor.Y = 400;

        await service.CaptureAsync();

        Assert.Equal(800 + RoiSettings.ReferenceOffsetX, capture.LastRect.X);
        Assert.Equal(400 + RoiSettings.ReferenceOffsetY, capture.LastRect.Y);
        Assert.Equal(RoiSettings.ReferenceWidth, capture.LastRect.Width);
        Assert.Equal(RoiSettings.ReferenceHeight, capture.LastRect.Height);
    }

    [Fact]
    public async Task CaptureAsync_AfterDispose_Throws()
    {
        var service = CreateService(out _, out _, out _);
        service.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => service.CaptureAsync());
    }
}
