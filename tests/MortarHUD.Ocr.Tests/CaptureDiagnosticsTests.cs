using MortarHUD.Capture;
using MortarHUD.Capture.Diagnostics;
using MortarHUD.Core.Configuration;
using MortarHUD.Core.Diagnostics;
using OpenCvSharp;
using Xunit;

namespace MortarHUD.Ocr.Tests;

[Collection("OCR")]
public class CaptureDiagnosticsTests
{
    [Fact]
    public void TemporaryCollectionStopsAndNeverAttachesPreviousImages()
    {
        var directory = Path.Combine(Path.GetTempPath(), "MortarHUD.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var settings = new DebugSettings();
            var observer = new CaptureDiagnostics(new DebugArtifactWriter(directory), () => settings, _ => { });
            using var image = new Mat(10, 10, MatType.CV_8UC3, Scalar.All(40));
            observer.CollectNext(2);
            for (var i = 0; i < 3; i++)
            {
                observer.BeginRequest();
                Assert.Null(observer.AttachImages(CaptureOutcome.Failed("BUSY", TimeSpan.Zero)).RawImagePath);
                observer.OnRawRoi(image);
                observer.OnProcessed("Tesseract", "A", image);
                observer.OnProcessed("Tesseract", "C", image);
                var outcome = observer.AttachImages(CaptureOutcome.Failed("PARSE_FAILED", TimeSpan.Zero));
                observer.WriteResult(outcome, false);
                if (i < 2)
                {
                    Assert.True(File.Exists(outcome.RawImagePath));
                    Assert.True(File.Exists(outcome.ProcessedImagePath));
                }
                else Assert.Null(outcome.RawImagePath);
            }
            Assert.Equal(2, Directory.GetFiles(directory, "*_result.json").Length);
            Assert.Equal(6, Directory.GetFiles(directory, "*.png").Length);
            Assert.False(settings.Enabled);
            Assert.False(settings.SaveRawRoi);
        }
        finally
        {
            // 只清理本测试创建的唯一目录，不触及用户诊断目录。
            foreach (var file in Directory.GetFiles(directory)) File.Delete(file);
            Directory.Delete(directory);
        }
    }
}
