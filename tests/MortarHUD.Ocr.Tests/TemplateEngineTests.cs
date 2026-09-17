using MortarHUD.Capture.ImageProcessing;
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
    /// 它会立刻变红，提醒去重新生成字形库——这正是当初缺 2 和 3 的原因，
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
            + "dotnet run --project tools/MortarHUD.Benchmark -- --gen-from-debug");
    }

    /// <summary>
    /// 字形库必须覆盖十位数字和小数点——不管基准集里有没有出现过。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 这条<strong>不能</strong>跟着 fixtures 走。fixture 只有 3 张，坐标数字的并集
    /// 恰好是 <c>{0,1,4,5,6,7,8,9}</c>，跟着样本走的话「缺 2 和 3」是绿的我。
    /// 缺一个数字不会报错，只会把「x102.3」读成「x10?.3」然后整条判失败——
    /// 静默失败比崩溃难查得多。
    /// </para>
    /// <para>
    /// 所以这里把要求钉死成十位数字全齐，跟样本无关。
    /// 2 和 3 的样本来自运行期采集记录（<c>--gen-from-debug</c>）。
    /// </para>
    /// </remarks>
    [Fact]
    public void AtlasCoversEveryDigitEvenIfTheFixturesDoNot()
    {
        using var engine = CreateEngine();
        Assert.True(engine.IsAvailable, "字形库加载失败");

        var coverage = engine.Coverage.ToHashSet();
        var missing = "0123456789".Where(d => !coverage.Contains(d)).ToList();

        Assert.True(
            missing.Count == 0,
            $"字形库缺这些数字：{string.Join(" ", missing)}。一个数字认不出来，"
            + "含这个数字的坐标会整条判失败（不是猜错，是判失败）。"
            + $"当前覆盖：{engine.Coverage}。"
            + "重新生成：dotnet run --project tools/MortarHUD.Benchmark -- --gen-from-debug");
    }

    /// <summary>
    /// 小数点和两个轴字母也得有——少了它们每行都读不完整。
    /// </summary>
    [Fact]
    public void AtlasCoversTheDecimalPointAndAxisLetters()
    {
        using var engine = CreateEngine();
        Assert.True(engine.IsAvailable, "字形库加载失败");

        var coverage = engine.Coverage.ToHashSet();
        var missing = new[] { '.', 'x', 'y' }.Where(c => !coverage.Contains(c)).ToList();

        Assert.True(
            missing.Count == 0,
            $"字形库缺这些字符：{string.Join(" ", missing)}。"
            + "没有小数点，解析器会因 RequireDecimalPoint 判失败。"
            + $"当前覆盖：{engine.Coverage}。");
    }

    /// <summary>
    /// 模板引擎要能把真实截图上的坐标读出来——不只是「认得字符」，而是真的读对。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 上面几条只查覆盖范围，覆盖对了不等于切得对、对得准：模板生成器的分割逻辑
    /// 和运行时共用一份实现，一旦哪里改了，形状对不上会表现为<em>识别率下降</em>
    /// 而不是报错。这条把真实截图跑一遍，才能拦住那种退化。
    /// </para>
    /// <para>
    /// 引擎收的是**已预处理**的图（预处理由识别链路负责），所以这里先跑 PipelineA。
    /// </para>
    /// <para>
    /// <strong>已知缺口：fixture 003 的 x 行读不对</strong>（读出 <c>x1107.6161</c>，
    /// 多出一个字符）。原因是那张截图的 ROI 把文字的左端切掉了，模板引擎按连通域切分
    /// 就会多数出一块。这是<em>既有问题</em>，换用旧字形库结果一模一样
    /// （旧库覆盖 <c>.01456789xy</c>，同样读成 <c>x1107.6161</c>），不是补齐 2、3 引入的。
    /// 修它要动 fixture 的 <c>labelBounds</c> 或换一张截图，属于单独一件事，
    /// 在修好之前这条断言只要求「其余 fixture 读得对」，免得缺口被测试掩盖掉。
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ReadsCoordinatesOffTheRealScreenshots()
    {
        // ROI 切掉了文字左端、已知读不对的 fixture。修好之后从这里删掉。
        var knownUnreadable = new HashSet<string>(StringComparer.Ordinal) { "003" };

        using var engine = CreateEngine();
        Assert.True(engine.IsAvailable, "字形库加载失败");

        var preprocessor = new PipelineA();
        var failures = new List<string>();
        var read = 0;

        foreach (var fixture in FixtureRepository.Load())
        {
            using var roi = FixtureRepository.LoadRoi(fixture);
            using var processed = preprocessor.Process(roi);

            var result = await engine.RecognizeAsync(processed, CancellationToken.None);

            var text = result.RawText.Replace("\r", "");

            // 读出来的文本里应当能找到这两行。多余的行（地图噪点）不影响判断。
            var expectedY = $"y{fixture.ExpectedY:0.00}";
            var expectedX = $"x{fixture.ExpectedX:0.00}";

            var ok = text.Contains(expectedY, StringComparison.Ordinal)
                     && text.Contains(expectedX, StringComparison.Ordinal);

            if (ok)
            {
                read++;
                continue;
            }

            if (knownUnreadable.Contains(fixture.Id))
            {
                continue;
            }

            failures.Add(
                $"fixture {fixture.Id}: 期望包含 \"{expectedY}\" 与 \"{expectedX}\"，"
                + $"实际读出 \"{text.Replace("\n", " | ")}\"");
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));

        // 别让整个基准集都被加进白名单——那样这条测试就废了。
        var readable = FixtureRepository.Load().Count - knownUnreadable.Count;
        Assert.True(
            read == readable,
            $"应当有 {readable} 个 fixture 读得出来，实际 {read} 个。"
            + "如果是因为某张截图换了，请一并更新白名单并在注释里写清原因。");
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
