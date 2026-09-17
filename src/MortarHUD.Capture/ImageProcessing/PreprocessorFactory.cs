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
    /// 只跑 A + C，<strong>不含 B</strong>——这是 Benchmark 量出来的结论，不是拍脑袋。
    /// </para>
    /// <para>
    /// 基准数据（<c>docs/ocr-benchmark.md</c>，三张实机截图，**已抹光标锚点**的线上路径）：
    /// C 3/3、B 2/3、A 1/3，A + C 交叉验证后 3/3。
    /// 注意「已抹光标锚点」这个前提——benchmark 曾经漏传光标位置，
    /// 量到的是线上不存在的配置，那时的 A/C 结论是反的。
    /// </para>
    /// <para>
    /// B 每次要多花约 40ms 却不贡献任何正确答案，直接拖垮 TDD §40 的 &lt;100ms 目标，
    /// 因此从 Auto 里摘掉。它仍然保留在设置页的下拉框里，遇到新地图可以手动试。
    /// </para>
    /// <para>
    /// 待定：当前这批 fixture 上 A 一次都没独立命中过（1/3），
    /// 跑它只是多花约 50ms 并带来「两票矛盾」的风险。
    /// 是继续用它做交叉验证、还是缩成单跑 C，等基准集扩到 30 张再定。
    /// </para>
    /// </remarks>
    public static IReadOnlyList<IImagePreprocessor> AutoCandidates { get; } =
    [
        new PipelineA(),
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
