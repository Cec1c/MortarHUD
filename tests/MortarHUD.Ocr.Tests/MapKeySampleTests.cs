using System.Text.Json;
using MortarHUD.Capture.ImageProcessing;
using MortarHUD.Capture.Ocr;
using MortarHUD.Core.Configuration;
using MortarHUD.Core.Parsing;
using MortarHUD.Core.Validation;
using OpenCvSharp;
using Xunit;

namespace MortarHUD.Ocr.Tests;

[Collection("OCR")]
public class MapKeySampleTests
{
    [Fact]
    public async Task ClearFramesAreReadAndTooltipsOrClosedMapsAreRejected()
    {
        var directory = Path.Combine(FixtureRepository.RepoRoot, "tests", "Fixtures", "m-key-20260922");
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "manifest.json")));
        var engines = OcrEngineFactory.Resolve("Auto", new OcrSettings(), FixtureRepository.TessdataDirectory);
        try
        {
            Assert.All(engines, engine => Assert.IsType<TesseractOcrEngine>(engine));
            var recognizer = new CoordinateRecognizer(engines, PreprocessorFactory.AutoCandidates,
                new CoordinateTextParser(), new CoordinateValidator());
            var failures = new List<string>();
            foreach (var row in manifest.RootElement.EnumerateArray())
            {
                var name = row.GetProperty("file").GetString()!;
                using var image = Cv2.ImDecode(File.ReadAllBytes(Path.Combine(directory, name)), ImreadModes.Color);
                var result = await recognizer.RecognizeAsync(image, CancellationToken.None, new(-5, 74));
                var expectedX = row.GetProperty("expectedX");
                if (!result.Success && row.TryGetProperty("requiredSuccess", out var required) && !required.GetBoolean())
                    continue; // 已知歧义允许拒绝，绝不能把另一条的错误值投下去后采纳。
                if (expectedX.ValueKind == JsonValueKind.Null)
                {
                    if (result.Success) failures.Add($"{name}: 无坐标画面被接受为 {result.Coordinate}");
                }
                else if (!result.Success || result.Coordinate is not { } c
                    || Math.Abs(c.X - expectedX.GetDouble()) > .005
                    || Math.Abs(c.Y - row.GetProperty("expectedY").GetDouble()) > .005)
                    failures.Add($"{name}: {result.Error} / {result.Coordinate}");
            }
            Assert.Empty(failures);
        }
        finally { foreach (var engine in engines) engine.Dispose(); }
    }

    [Fact]
    public async Task BlueMapEdgeNoLongerDropsLeading110InTheFullBlockReader()
    {
        var path = Path.Combine(FixtureRepository.RepoRoot, "tests", "Fixtures", "m-key-20260922", "2026-09-22_010040_567_roi.png");
        using var roi = Cv2.ImDecode(File.ReadAllBytes(path), ImreadModes.Color);
        var engines = OcrEngineFactory.Resolve("Auto", new OcrSettings(), FixtureRepository.TessdataDirectory);
        try
        {
            var parser = new CoordinateTextParser();
            var validator = new CoordinateValidator();
            var previous = await new CoordinateRecognizer(engines, [new PipelineC(false)], parser, validator)
                .RecognizeAsync(roi, CancellationToken.None, new(-5, 74));
            Assert.Equal("PIPELINE_DISAGREEMENT", previous.Error);
            var current = await new CoordinateRecognizer(engines, [new PipelineC()], parser, validator)
                .RecognizeAsync(roi, CancellationToken.None, new(-5, 74));
            Assert.True(current.Success);
            Assert.Equal(110.38, current.Coordinate!.Value.Y);
            Assert.All(current.Attempts, a => Assert.True(a.Success));
        }
        finally { foreach (var engine in engines) engine.Dispose(); }
    }
}
