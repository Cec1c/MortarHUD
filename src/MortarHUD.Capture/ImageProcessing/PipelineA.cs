using OpenCvSharp;

namespace MortarHUD.Capture.ImageProcessing;

/// <summary>
/// Pipeline A（TDD §14）：灰度 → 3x 放大 → 对比度拉伸 → Otsu 全局阈值。
/// </summary>
/// <remarks>
/// <para>
/// 最简单、最快的一条，适合「暗地图 + 白字」这种整体对比度良好的场景。
/// 仓库里的三张实机截图大多属于这一类。
/// </para>
/// <para>
/// 用 <see cref="ThresholdTypes.BinaryInv"/>：亮于阈值的文字变成 0（黑），
/// 暗于阈值的背景变成 255（白），一步就得到 OCR 要的黑字白底。
/// </para>
/// </remarks>
public sealed class PipelineA : PreprocessorBase
{
    public override string Name => "A";

    protected override Mat Binarize(Mat upscaledGray)
    {
        // 先拉满对比度，让 Otsu 在两个峰之间切得更稳。
        StretchContrast(upscaledGray);

        using var raw = new Mat();
        Cv2.Threshold(
            upscaledGray,
            raw,
            0,
            255,
            ThresholdTypes.BinaryInv | ThresholdTypes.Otsu);

        // 抹掉地图网格线：它比坐标文字还亮，Otsu 一定把它一起当字，
        // 而它穿过 ROI 时会啃掉 y、x 两个轴字母。详见 RemoveLongLines。
        return RemoveLongLines(raw, LineKernelLength(raw));
    }
}
