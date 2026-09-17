using MortarHUD.Core.Configuration;

namespace MortarHUD.Capture.ImageProcessing;

/// <summary>按名字造预处理流水线（TDD §14 设置项）。</summary>
public static class PreprocessorFactory
{
    /// <summary>内置流水线。</summary>
    public static IReadOnlyList<IImagePreprocessor> All { get; } =
    [
        new PipelineA(),
        new PipelineB(),
        new PipelineC(),
    ];

    /// <summary>
    /// Auto 模式实际跑的流水线。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 只跑 C，一条二值化流水线。
    /// </para>
    /// <para>
    /// 交叉验证并没有取消，只是换了多样性来源：<strong>引擎侧</strong>会用两种分页模式
    /// （6 与 11）各读一遍，见 <c>OcrEngineFactory</c>。原来靠「A + C 两种二值化」凑两票，
    /// 但实机数据表明 A 是弱的那一条——误读末位、丢前导数字都出在它身上。
    /// </para>
    /// <para>
    /// 117 份实机采集回放（TDD 要求的一致性规则：成功读数必须全部一致）：
    /// </para>
    /// <list type="bullet">
    /// <item>A + C：成功 65/117，冲突 16 次。</item>
    /// <item><strong>C（两种分页模式）：成功 80/117，冲突 5 次。</strong></item>
    /// <item>A + C + 第三种读数：冲突反而涨到 23 次——多一个弱投票者只是多一次吵架机会。</item>
    /// </list>
    /// <para>
    /// B 每次要多花约 40ms 却不贡献任何正确答案，直接拖垮 TDD §40 的 &lt;100ms 目标。
    /// A 和 B 都仍然保留在设置页的下拉框里，遇到新地图可以手动试。
    /// </para>
    /// </remarks>
    public static IReadOnlyList<IImagePreprocessor> AutoCandidates { get; } =
    [
        new PipelineC(),
    ];

    /// <summary>按设置解析出一条流水线。名字无法识别时回退到 A。</summary>
    public static IImagePreprocessor Resolve(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Equals(PreprocessorNames.Auto, StringComparison.OrdinalIgnoreCase))
        {
            return All[0];
        }

        foreach (var preprocessor in All)
        {
            if (preprocessor.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return preprocessor;
            }
        }

        return All[0];
    }

    /// <summary>Auto 模式下要依次尝试的全部流水线。</summary>
    public static IReadOnlyList<IImagePreprocessor> ResolveCandidates(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Equals(PreprocessorNames.Auto, StringComparison.OrdinalIgnoreCase))
        {
            return AutoCandidates;
        }

        return [Resolve(name)];
    }
}
