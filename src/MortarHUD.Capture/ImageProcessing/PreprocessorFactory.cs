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

    /// <summary>Auto 使用 C 的两种颜色投影，各自配合 PSM 6 / 11，所有有效读数必须一致。</summary>
    /// <remarks>
    /// 纯通道最小值压掉蓝色边框，但会把少量细笔画 3 读成 5；
    /// 保留一半灰度能守住这类分歧。不能只选通过率较高的版本。
    /// 两者仍使用同样的顶帽/双门限；A、B 保留供手动诊断，不参与 Auto。
    /// 具体回放与取舍见 docs/2026-09-22-acceptance.md。
    /// </remarks>
    public static IReadOnlyList<IImagePreprocessor> AutoCandidates { get; } =
    [
        new PipelineC(),
        new PipelineC(neutralWeight: 0.5),
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
