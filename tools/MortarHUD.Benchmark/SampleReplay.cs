using System.Text.Json;
using MortarHUD.Capture.Ocr;
using MortarHUD.Capture.ImageProcessing;
using MortarHUD.Capture.ScreenCapture;
using MortarHUD.Core.Configuration;
using MortarHUD.Core.Parsing;
using MortarHUD.Core.Validation;
using OpenCvSharp;

namespace MortarHUD.Benchmark;

/// <summary>离线回放同一张整屏里的 ROI，不把旧 OCR 结果当作真值。</summary>
internal static class SampleReplay
{
    public static async Task<int> RunAsync(string directory, string output, string tessdata, string? prefix, bool legacyGray = false)
    {
        var settings = new OcrSettings();
        var engines = OcrEngineFactory.Resolve(OcrEngineNames.Auto, settings, tessdata);
        try
        {
            var recognizer = new CoordinateRecognizer(engines, legacyGray ? [new PipelineC(false)] : PreprocessorFactory.AutoCandidates,
                new CoordinateTextParser(), new CoordinateValidator());
            var rows = new List<object>();
            var successes = 0;
            foreach (var path in Directory.EnumerateFiles(directory, "*_sample.json").Order())
            {
                if (prefix is not null && !Path.GetFileName(path).StartsWith(prefix, StringComparison.Ordinal)) continue;
                using var metadata = JsonDocument.Parse(File.ReadAllText(path));
                var cursor = metadata.RootElement.GetProperty("cursor");
                var screen = metadata.RootElement.GetProperty("screen");
                var x = cursor.GetProperty("x").GetInt32();
                var y = cursor.GetProperty("y").GetInt32();
                var bounds = new System.Drawing.Rectangle(screen.GetProperty("x").GetInt32(),
                    screen.GetProperty("y").GetInt32(), screen.GetProperty("w").GetInt32(), screen.GetProperty("h").GetInt32());
                var rect = RoiCalculator.Compute(x, y, new RoiSettings(), bounds.Height, bounds);
                using var frame = Cv2.ImDecode(File.ReadAllBytes(path.Replace("_sample.json", "_frame.png")), ImreadModes.Color);
                using var roi = new Mat(frame, new Rect(rect.X - bounds.X, rect.Y - bounds.Y, rect.Width, rect.Height));
                var result = await recognizer.RecognizeAsync(roi, CancellationToken.None, new(x - rect.X, y - rect.Y));
                if (result.Success) successes++;
                rows.Add(new { file = Path.GetFileName(path), cursorX = x, cursorY = y,
                    result.Success, result.Coordinate, result.Error,
                    attempts = CoordinateRecognizer.DescribeAttempts(result.Attempts) });
            }
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
            File.WriteAllText(output, JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"回放 {rows.Count} 帧，识别通过 {successes} 帧（未经人工标注，不等于正确率）。报告：{output}");
            return rows.Count == 0 ? 2 : 0;
        }
        finally { foreach (var engine in engines) engine.Dispose(); }
    }
}
