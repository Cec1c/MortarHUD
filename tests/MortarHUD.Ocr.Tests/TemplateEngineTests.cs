using MortarHUD.Capture.Ocr;
using Xunit;

namespace MortarHUD.Ocr.Tests;

/// <summary>
/// 模板匹配引擎与它依赖的字形库。
/// </summary>
/// <remarks>
/// <para>
/// 这个引擎只在语言包缺失时才会被创建（<c>OcrEngineFactory</c> 的兜底分支），
/// 生产路径走的是 Tesseract，所以它长期没有测试也没出过事——但字形库是
/// <strong>生成</strong>出来的，生成器一改就可能悄悄做差，而没有任何东西会告诉你。
/// </para>
/// <para>
/// 这组测试就是那道网：字形库覆盖不到某个字符时，那个字符会被判成 <c>?</c>，
/// 整次识别失败——宁可失败也不猜值，这是对的一侧，但<strong>缺口本身要被看见</strong>。
/// </para>
/// </remarks>
[Collection("OCR")]
public class TemplateEngineTests
{
    private static TemplateOcrEngine CreateEngine()
        => new(FixtureRepository.GlyphAtlasDirectory);

    [Fact]
    public void AtlasLoads()
    {
        using var engine = CreateEngine();

        Assert.True(engine.IsAvailable, $"字形库加载失败：{engine.AtlasDirectory}");
        Assert.False(string.IsNullOrWhiteSpace(engine.Coverage));
    }

    /// <summary>
    /// 基准集里出现过的每一个字符，字形库都得覆盖。
    /// </summary>
    /// <remarks>
    /// 这条是跟着样本走的：往 fixtures 里加一张坐标含 2 或 3 的截图，
    /// 它会立刻变红，提醒去重新生成字形库——这正是目前缺 2 和 3 的原因，
    /// 只是当时没有东西拦着。
    /// </remarks>
    [Fact]
    public void AtlasCoversEveryCharacterTheFixturesNeed()
    {
        using var engine = CreateEngine();
        Assert.True(engine.IsAvailable, "字形库加载失败");

        var coverage = engine.Coverage.ToHashSet();

        // 引擎实际要读的就是这两行，字符集从这里推最贴近真实输入。
        var needed = FixtureRepository.Load()
            .SelectMany(f => $"y{f.ExpectedY:0.00}x{f.ExpectedX:0.00}")
            .Distinct()
            .Order()
            .ToList();

        var missing = needed.Where(c => !coverage.Contains(c)).ToList();

        Assert.True(
            missing.Count == 0,
            $"基准集需要这些字符但字形库没有：{string.Join(" ", missing)}。"
            + $"当前覆盖：{engine.Coverage}。重新生成："
            + "dotnet run --project tools/MortarHUD.Benchmark -- --gen-templates");
    }

    /// <summary>模板库不认识的字符要输出 `?` 而不是猜一个数字出来。</summary>
    /// <remarks>
    /// 猜值会静默改变坐标——7 没认出来，「x107.66」就变成「x10.66」，
    /// 那是 7 个坐标单位的误差。插入非法字符让解析器判失败，才是安全的那一侧。
    /// </remarks>
    [Fact]
    public void CoverageNeverContainsAnythingTheParserWouldReject()
    {
        using var engine = CreateEngine();
        Assert.True(engine.IsAvailable, "字形库加载失败");

        // 解析器接受的就是白名单里那些字符；字形库多认出别的字符没有意义，
        // 多出来的多半是生成器把噪声也切进去当字形了。
        const string parserAlphabet = "0123456789xy.:-";

        var unexpected = engine.Coverage.Where(c => !parserAlphabet.Contains(c)).ToList();

        Assert.True(
            unexpected.Count == 0,
            $"字形库里混进了解析器不认的字符：{string.Join(" ", unexpected)}。"
            + "多半是生成器把地图纹理切进去当字形了。");
    }
}
