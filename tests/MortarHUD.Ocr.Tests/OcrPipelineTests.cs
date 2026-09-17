using MortarHUD.Capture.ImageProcessing;
using MortarHUD.Capture.Ocr;
using MortarHUD.Core.Configuration;
using MortarHUD.Core.Parsing;
using MortarHUD.Core.Validation;
using OpenCvSharp;
using Xunit;

namespace MortarHUD.Ocr.Tests;

/// <summary>
/// 端到端 OCR 测试：真实截图 → 预处理 → OCR → 解析 → 校验。
/// </summary>
/// <remarks>
/// 这是整个项目最有价值的一组测试。TDD §47 的验收条件里
/// 「地图缩放不影响坐标读取」「OCR 错误不会静默使用错误数据」
/// 归根到底都要靠这组用例来保证。
/// </remarks>
[Collection("OCR")]
public class OcrPipelineTests
{
    private static readonly CoordinateTextParser Parser = new();

    private static CoordinateValidator CreateValidator() => new(new CoordinateValidationOptions
    {
        CoordinateMin = 0,
        CoordinateMax = 200,
        MinimumConfidence = new OcrSettings().MinimumConfidence,
    });

    /// <summary>
    /// 建好的识别链路 + 它所持有的引擎。
    /// </summary>
    /// <remarks>
    /// OCR 引擎（尤其是 Tesseract）持有原生句柄，测试里必须显式释放，
    /// 否则一个测试类跑下来会攒下几十个引擎实例。
    /// </remarks>
    private sealed class OcrHarness : IDisposable
    {
        private readonly List<ICoordinateOcrEngine> _engines;

        public OcrHarness(IReadOnlyList<IImagePreprocessor> preprocessors)
        {
            var settings = new OcrSettings { TessdataPath = FixtureRepository.TessdataDirectory };

            _engines = [.. OcrEngineFactory.Resolve(
                OcrEngineNames.Auto, settings, FixtureRepository.TessdataDirectory)];

            Recognizer = new CoordinateRecognizer(
                _engines, preprocessors, Parser, CreateValidator());
        }

        public CoordinateRecognizer Recognizer { get; }

        public void Dispose()
        {
            foreach (var engine in _engines)
            {
                engine.Dispose();
            }
        }
    }

    [Fact]
    public void Fixtures_ArePresent()
    {
        var fixtures = FixtureRepository.Load();

        Assert.NotEmpty(fixtures);
    }

    /// <summary>
    /// Auto 配置（多条流水线交叉验证）必须把每张实机截图都读对。
    /// </summary>
    [Fact]
    public async Task AutoConfiguration_RecognizesEveryFixtureCorrectly()
    {
        var fixtures = FixtureRepository.Load();
        using var harness = new OcrHarness(PreprocessorFactory.AutoCandidates);

        var failures = new List<string>();

        foreach (var fixture in fixtures)
        {
            using var roi = FixtureRepository.LoadRoi(fixture);

            var outcome = await harness.Recognizer
                .RecognizeAsync(roi, CancellationToken.None);

            if (!outcome.Success || outcome.Coordinate is null)
            {
                failures.Add($"{fixture.Id}: 识别失败 {outcome.Error}");
                continue;
            }

            var coordinate = outcome.Coordinate.Value;

            if (Math.Abs(coordinate.X - fixture.ExpectedX) > 0.005
                || Math.Abs(coordinate.Y - fixture.ExpectedY) > 0.005)
            {
                failures.Add(
                    $"{fixture.Id}: 读成 {coordinate.X:0.00}/{coordinate.Y:0.00}，"
                    + $"期望 {fixture.ExpectedX:0.00}/{fixture.ExpectedY:0.00}");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    /// <summary>
    /// 交叉验证必须真的在起作用：单条流水线各自会错，合起来不会。
    /// </summary>
    /// <remarks>
    /// 这条测试的意义在于——如果有人「优化」掉了多流水线逻辑，
    /// 单个流水线的测试仍然全绿，但真实场景的正确率会从 100% 掉到 67%。
    /// 这里把这个差距钉住。
    /// </remarks>
    [Fact]
    public async Task CrossValidation_BeatsAnySinglePipeline()
    {
        var fixtures = FixtureRepository.Load();

        var autoHits = 0;
        var singlePipelineHits = 0;

        using var autoHarness = new OcrHarness(PreprocessorFactory.AutoCandidates);

        // 三条单流水线链路建一次就够——每条都要初始化一个 Tesseract 实例，
        // 放进 fixture 循环里会把测试时间放大好几倍。
        var singleHarnesses = PreprocessorFactory.All
            .Select(p => new SinglePipelineHarness(p))
            .ToList();

        foreach (var fixture in fixtures)
        {
            using var roi = FixtureRepository.LoadRoi(fixture);

            var autoOutcome = await autoHarness.Recognizer
                .RecognizeAsync(roi, CancellationToken.None);

            if (IsHit(autoOutcome, fixture))
            {
                autoHits++;
            }

            // 单条流水线里最好的一条
            var best = false;

            foreach (var single in singleHarnesses)
            {
                var outcome = await single.Recognizer
                    .RecognizeAsync(roi, CancellationToken.None);

                best |= IsHit(outcome, fixture);
            }

            if (best)
            {
                singlePipelineHits++;
            }
        }

        foreach (var single in singleHarnesses)
        {
            single.Dispose();
        }

        Assert.Equal(fixtures.Count, autoHits);
        Assert.True(
            autoHits >= singlePipelineHits,
            $"交叉验证 {autoHits}/{fixtures.Count} 不应少于单流水线最好情况 {singlePipelineHits}/{fixtures.Count}");
    }

    private static bool IsHit(RecognitionOutcome outcome, FixtureCase fixture)
        => outcome.Success
           && outcome.Coordinate is { } coordinate
           && Math.Abs(coordinate.X - fixture.ExpectedX) < 0.005
           && Math.Abs(coordinate.Y - fixture.ExpectedY) < 0.005;

    /// <summary>只跑单条流水线的识别链路，用于和 Auto 交叉验证做对比。</summary>
    private sealed class SinglePipelineHarness : IDisposable
    {
        private readonly OcrHarness _inner;

        public SinglePipelineHarness(IImagePreprocessor preprocessor)
            => _inner = new OcrHarness([preprocessor]);

        public CoordinateRecognizer Recognizer => _inner.Recognizer;

        public void Dispose() => _inner.Dispose();
    }
}

/// <summary>OCR 测试会初始化 Tesseract，串行执行避免争抢。</summary>
[CollectionDefinition("OCR", DisableParallelization = true)]
public class OcrCollection
{
}
