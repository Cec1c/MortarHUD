using MortarHUD.Capture.ImageProcessing;
using MortarHUD.Capture.Ocr;
using MortarHUD.Core.Models;
using MortarHUD.Core.Parsing;
using MortarHUD.Core.Validation;
using OpenCvSharp;
using Xunit;

namespace MortarHUD.Ocr.Tests;

/// <summary>
/// 识别失败时的诊断信息测试。
/// </summary>
/// <remarks>
/// 动机来自一次真实的排查困境：用户那边间歇性识别失败，日志里只有一句
/// <c>X_NOT_FOUND</c>，完全看不出是「ROI 没框住文字」还是「读到了但格式不对」——
/// 而这两者的排查方向正好相反。
///
/// 这组用例把「失败原因必须能区分这两类」这件事固化下来。
/// </remarks>
[Collection("OCR")]
public class FailureDiagnosisTests
{
    /// <summary>按脚本返回文本的桩引擎。</summary>
    private sealed class ScriptedEngine : ICoordinateOcrEngine
    {
        private readonly string _text;

        public ScriptedEngine(string text, string name = "Stub")
        {
            _text = text;
            Name = name;
        }

        public string Name { get; }

        public Task<CoordinateOcrResult> RecognizeAsync(Mat input, CancellationToken cancellationToken)
            => Task.Run(() => new CoordinateOcrResult
            {
                Success = !string.IsNullOrEmpty(_text),
                RawText = _text,
                Confidence = 0.9,
            }, cancellationToken);

        public void Dispose()
        {
        }
    }

    private static CoordinateRecognizer CreateRecognizer(params ICoordinateOcrEngine[] engines)
        => new(
            engines,
            [PreprocessorFactory.AutoCandidates[0]],
            new CoordinateTextParser(),
            new CoordinateValidator(new CoordinateValidationOptions { MinimumConfidence = 0.0 }));

    private static Mat CreateRoi() => new(140, 150, MatType.CV_8UC3, Scalar.All(40));

    /// <summary>
    /// 引擎一个字都没读出来 —— 说明 ROI 里根本没有坐标文字，该去挪 ROI。
    /// </summary>
    [Fact]
    public async Task AllEnginesReturnNothing_ReportsNoTextInRoi()
    {
        using var roi = CreateRoi();
        var recognizer = CreateRecognizer(new ScriptedEngine(""));

        var outcome = await recognizer.RecognizeAsync(roi, CancellationToken.None);

        Assert.False(outcome.Success);
        Assert.Equal("NO_TEXT_IN_ROI", outcome.Error);
    }

    /// <summary>
    /// 读到了文字但不符合坐标格式 —— 说明 ROI 基本是对的，问题在解析或预处理。
    /// </summary>
    [Fact]
    public async Task EnginesReturnTextThatDoesNotParse_ReportsParseFailure()
    {
        using var roi = CreateRoi();
        var recognizer = CreateRecognizer(new ScriptedEngine("地图 建筑 树林"));

        var outcome = await recognizer.RecognizeAsync(roi, CancellationToken.None);

        Assert.False(outcome.Success);
        Assert.NotNull(outcome.Error);
        Assert.StartsWith("PARSE_FAILED", outcome.Error, StringComparison.Ordinal);
    }

    /// <summary>缺少小数点这类拒绝，也要能在错误串里看出来。</summary>
    [Fact]
    public async Task MissingDecimalPoint_ErrorMentionsXNotFound()
    {
        using var roi = CreateRoi();
        var recognizer = CreateRecognizer(new ScriptedEngine("x10766 y11454"));

        var outcome = await recognizer.RecognizeAsync(roi, CancellationToken.None);

        Assert.False(outcome.Success);
        Assert.Contains("X_NOT_FOUND", outcome.Error, StringComparison.Ordinal);
    }

    /// <summary>两条流水线给出不同答案时不能被当成「没读到」。</summary>
    [Fact]
    public async Task DisagreeingPipelines_ReportDisagreement()
    {
        using var roi = CreateRoi();
        var recognizer = CreateRecognizer(
            new ScriptedEngine("y109.78 x98.09", "A"),
            new ScriptedEngine("y109.78 x98.10", "B"));

        var outcome = await recognizer.RecognizeAsync(roi, CancellationToken.None);

        Assert.False(outcome.Success);
        Assert.Equal("PIPELINE_DISAGREEMENT", outcome.Error);
    }

    /// <summary>
    /// 日志里必须能看到引擎到底吐了什么，否则等于没有诊断信息。
    /// </summary>
    [Fact]
    public async Task DescribeAttempts_IncludesRawOcrText()
    {
        using var roi = CreateRoi();
        var recognizer = CreateRecognizer(new ScriptedEngine("y109.78 x98.09"));

        var outcome = await recognizer.RecognizeAsync(roi, CancellationToken.None);

        Assert.True(outcome.Success);

        var description = CoordinateRecognizer.DescribeAttempts(outcome.Attempts);

        Assert.Contains("y109.78", description, StringComparison.Ordinal);
        Assert.Contains("98.09", description, StringComparison.Ordinal);
    }

    /// <summary>引擎没吐文字时，描述里要明确写「空」，而不是留一段空白让人以为是日志截断。</summary>
    [Fact]
    public void DescribeAttempts_EmptyText_IsMarkedExplicitly()
    {
        var attempts = new[]
        {
            new RecognitionAttempt
            {
                Engine = "Tesseract",
                Pipeline = "A",
                RawText = "",
                Error = "NO_TEXT_RECOGNIZED",
            },
        };

        var description = CoordinateRecognizer.DescribeAttempts(attempts);

        Assert.Contains("(空)", description, StringComparison.Ordinal);
        Assert.Contains("NO_TEXT_RECOGNIZED", description, StringComparison.Ordinal);
    }
}
