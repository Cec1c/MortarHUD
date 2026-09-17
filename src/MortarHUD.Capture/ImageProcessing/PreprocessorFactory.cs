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
    /// 只跑 A + C，<strong>不含 B</strong>——这是 Benchmark 量出来的结论，不是拍脑袋：
    /// 在仓库的三张实机截图上，B（自适应阈值）0/3 正确，而 A 和 C 各 2/3 且失败样本互补
    /// （001 两者都过，002 只有 C 过，003 只有 A 过），三者交叉验证后达到 3/3。
    /// </para>
    /// <para>
    /// B 每次要多花约 40ms 却不贡献任何正确答案，直接拖垮 TDD §40 的 &lt;100ms 目标，
    /// 因此从 Auto 里摘掉。它仍然保留在设置页的下拉框里，遇到新地图可以手动试。
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
