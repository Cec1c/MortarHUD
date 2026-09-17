using MortarHUD.Capture.ImageProcessing;
using OpenCvSharp;
using Xunit;

namespace MortarHUD.Ocr.Tests;

/// <summary>预处理流水线测试（TDD §14）。</summary>
[Collection("OCR")]
public class PreprocessorTests
{
    /// <summary>
    /// 所有流水线都必须输出「黑字白底」，且必须留出安静区。
    /// </summary>
    /// <remarks>
    /// 极性搞反是这块最容易犯又最难发现的错误——图看着「有字」，
    /// 但 Tesseract 在黑底白字上的识别率会明显下降，而且不会报错。
    /// 这里用「白底占多数」来钉住极性。
    /// </remarks>
    [Theory]
    [InlineData("A")]
    [InlineData("B")]
    [InlineData("C")]
    public void Process_ProducesBlackTextOnWhiteWithQuietZone(string pipelineName)
    {
        var fixture = FixtureRepository.Load().First();
        using var roi = FixtureRepository.LoadRoi(fixture);

        var preprocessor = PreprocessorFactory.Resolve(pipelineName);
        using var processed = preprocessor.Process(roi);

        Assert.Equal(MatType.CV_8UC1, processed.Type());
        Assert.True(processed.Width > roi.Width, "输出应比输入宽（四周补了安静区）");
        Assert.True(processed.Height > roi.Height, "输出应比输入高（四周补了安静区）");

        // 白底（255）必须占多数；如果极性反了，黑会占多数。
        var whiteRatio = Cv2.CountNonZero(processed) / (double)(processed.Rows * processed.Cols);
        Assert.True(whiteRatio > 0.80, $"白底只占 {whiteRatio:P1}，极性可能反了");

        // 安静区四边必须是纯白。
        Assert.Equal(255, processed.At<byte>(0, 0));
        Assert.Equal(255, processed.At<byte>(processed.Rows - 1, processed.Cols - 1));
    }

    /// <summary>三条流水线必须真的不同，否则「多流水线交叉验证」没有意义。</summary>
    [Fact]
    public void Pipelines_ProduceDifferentResults()
    {
        var fixture = FixtureRepository.Load().First();
        using var roi = FixtureRepository.LoadRoi(fixture);

        using var a = PreprocessorFactory.Resolve("A").Process(roi);
        using var c = PreprocessorFactory.Resolve("C").Process(roi);

        using var difference = new Mat();
        Cv2.Absdiff(a, c, difference);

        Assert.True(Cv2.CountNonZero(difference) > 0, "A 和 C 的输出完全一致");
    }

    [Fact]
    public void Process_EmptyInput_Throws()
    {
        var preprocessor = PreprocessorFactory.Resolve("A");
        using var empty = new Mat();

        Assert.Throws<ImagePreprocessingException>(() => preprocessor.Process(empty));
    }

    [Fact]
    public void Resolve_UnknownName_FallsBackToPipelineA()
    {
        var preprocessor = PreprocessorFactory.Resolve("不存在的流水线");

        Assert.Equal("A", preprocessor.Name);
    }

    [Fact]
    public void ResolveCandidates_ExplicitName_ReturnsSingle()
    {
        var candidates = PreprocessorFactory.ResolveCandidates("C");

        Assert.Single(candidates);
        Assert.Equal("C", candidates[0].Name);
    }

    /// <summary>
    /// Auto 模式下实际跑的流水线集合。
    /// </summary>
    /// <remarks>
    /// 这里刻意断言「不含 B」：B 在实机截图上 0/3 正确却要多花约 40ms，
    /// 拖垮 TDD §40 的 &lt;100ms 目标。它仍然可以手动选用。
    /// </remarks>
    [Fact]
    public void ResolveCandidates_Auto_ExcludesPipelineB()
    {
        var candidates = PreprocessorFactory.ResolveCandidates("Auto");

        Assert.Contains(candidates, p => p.Name == "A");
        Assert.Contains(candidates, p => p.Name == "C");
        Assert.DoesNotContain(candidates, p => p.Name == "B");
    }

    /// <summary>不同尺寸的 ROI 都要能处理（不同分辨率 + AutoScale 会改变 ROI 尺寸）。</summary>
    [Theory]
    [InlineData(150, 140)]
    [InlineData(225, 210)]
    [InlineData(75, 70)]
    public void Process_HandlesVaryingRoiSizes(int width, int height)
    {
        var fixture = FixtureRepository.Load().First();
        using var roi = FixtureRepository.LoadRoi(fixture);

        using var resized = new Mat();
        Cv2.Resize(roi, resized, new Size(width, height));

        foreach (var preprocessor in PreprocessorFactory.All)
        {
            using var processed = preprocessor.Process(resized);
            Assert.False(processed.Empty());
        }
    }
}
