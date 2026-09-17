using MortarHUD.Capture.ImageProcessing;
using MortarHUD.Capture.Ocr;
using MortarHUD.Core.Models;
using MortarHUD.Core.Parsing;
using MortarHUD.Core.Validation;
using OpenCvSharp;
using Xunit;

namespace MortarHUD.Ocr.Tests;

[Collection("OCR")]
public class ConfidencePolicyTests
{
    private sealed class Engine(params (string Text, double Confidence)[] results) : ICoordinateOcrEngine
    {
        private int _index;
        public string Name => "脚本";
        public void Dispose() { }
        public Task<CoordinateOcrResult> RecognizeAsync(Mat input, CancellationToken token)
        {
            var result = results[_index++];
            return Task.FromResult(new CoordinateOcrResult { Success = true,
                RawText = result.Text, Confidence = result.Confidence });
        }
    }

    private static async Task<RecognitionOutcome> Recognize(double threshold, params (string, double)[] results)
    {
        using var roi = new Mat(140, 150, MatType.CV_8UC3, Scalar.All(40));
        using var engine = new Engine(results);
        var pipelines = Enumerable.Range(0, results.Length).Select(_ => (IImagePreprocessor)new PipelineA()).ToArray();
        var recognizer = new CoordinateRecognizer([engine], pipelines, new CoordinateTextParser(),
            new CoordinateValidator(new CoordinateValidationOptions { MinimumConfidence = threshold }));
        return await recognizer.RecognizeAsync(roi, CancellationToken.None);
    }

    [Fact]
    public async Task FinalThresholdIsAppliedAfterAgreement()
    {
        var accepted = await Recognize(.6, ("x98.09 y109.78", 0), ("", 0));
        Assert.True(accepted.Success);
        Assert.Equal(.675, accepted.Confidence, 3);
        var rejected = await Recognize(.9, ("x98.09 y109.78", 0), ("", 0));
        Assert.False(rejected.Success);
        Assert.Null(rejected.Coordinate);
        Assert.Equal("LOW_CONFIDENCE", rejected.Error);
    }

    [Fact]
    public async Task LowConfidenceConflictCannotBeHiddenByMajority()
    {
        var result = await Recognize(.6, ("x98.09 y109.78", .99),
            ("x98.09 y109.78", .99), ("x98.19 y109.78", 0));
        Assert.False(result.Success);
        Assert.Equal("PIPELINE_DISAGREEMENT", result.Error);
    }

    [Theory]
    [InlineData("x9809 y10978")]
    [InlineData("x298.09 y109.78")]
    [InlineData("x98.09 y109.78 x99.09")]
    public async Task ConfidenceNeverRepairsInvalidCoordinates(string text)
    {
        var result = await Recognize(.6, (text, 1), (text, 1));
        Assert.False(result.Success);
        Assert.Null(result.Coordinate);
    }
}
