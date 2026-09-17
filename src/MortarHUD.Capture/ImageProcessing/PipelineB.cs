using OpenCvSharp;

namespace MortarHUD.Capture.ImageProcessing;

/// <summary>
/// Pipeline B（TDD §14）：灰度 → 4x 放大 → 自适应阈值 → 形态学闭运算。
/// </summary>
/// <remarks>
/// <para>
/// 全局阈值在「一块暗、一块亮」的 ROI 上会失效——本仓库的 fixture 002 就是这种情况，
/// 右半边是游戏世界的亮色水泥墙。自适应阈值按局部邻域算阈值，亮暗交界处也能稳定二值化。
/// </para>
/// <para>
/// <strong>为什么先取反：</strong>OpenCV 的 <c>adaptiveThreshold</c> 阈值是
/// <c>局部均值 − C</c>，它假定「暗字浅底」。直接用在白字暗底上时，
/// 背景像素本身就高于「均值 − C」，于是整张图被判成前景
/// （实测前景占比 72%，全是噪点）。先把灰度取反，文字变成暗、背景变成浅，
/// 才是该函数预期的输入分布。
/// </para>
/// <para>
/// <strong>为什么还要突出度门限：</strong>Benchmark 实测发现，只用自适应阈值时
/// 一张 ROI 会切出 20~38 个连通域（真实字形只有 6~7 个）——地图上任何一块
/// 「比周围亮」的区域都会变成前景。因此先用顶帽取出局部突出结构，
/// 再在它上面做自适应二值化，最后用突出度幅度门限收口。
/// </para>
/// </remarks>
public sealed class PipelineB : PreprocessorBase
{
    /// <summary>邻域边长，必须是奇数。4x 放大后笔画约 8~12px，41 大约覆盖 4 个笔画宽。</summary>
    private const int BlockSize = 41;

    /// <summary>从局部均值里减掉的常数。</summary>
    private const double Constant = 10.0;

    /// <summary>顶帽结构元边长（作用于 4x 放大后的图，约合原始尺度 6px）。</summary>
    private const int TopHatKernelSize = 25;

    public override string Name => "B";

    protected override double Scale => 4.0;

    protected override Mat Binarize(Mat upscaledGray)
    {
        // 先算顶帽，把「局部突出」的部分单独拎出来。
        using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(TopHatKernelSize, TopHatKernelSize));
        using var topHat = new Mat();
        Cv2.MorphologyEx(upscaledGray, topHat, MorphTypes.TopHat, kernel);

        // 门限 1：突出度幅度。文字够突出，地图干扰物不够。
        using var strengthMask = BuildStrengthMask(topHat);

        // 门限 2：在突出度图上做局部自适应二值化——这正是本流水线与 A/C 的区别所在。
        var inverted = new Mat();
        Cv2.BitwiseNot(topHat, inverted);

        // 用 BinaryInv：取反后的图里笔画是暗的，要让笔画变成 255（前景）才和强度掩码一致。
        // 用 Binary 会得到「白=背景」，后面一取交集就什么都不剩了。
        using var adaptiveMask = new Mat();
        Cv2.AdaptiveThreshold(
            inverted,
            adaptiveMask,
            255,
            AdaptiveThresholdTypes.GaussianC,
            ThresholdTypes.BinaryInv,
            BlockSize,
            Constant);

        inverted.Dispose();

        var whiteTextOnBlack = new Mat();
        Cv2.BitwiseAnd(adaptiveMask, strengthMask, whiteTextOnBlack);

        // 闭运算把笔画里被打断的地方接上——Tesseract 对断裂字符很敏感。
        using var closeKernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(3, 3));

        var closed = new Mat();
        Cv2.MorphologyEx(whiteTextOnBlack, closed, MorphTypes.Close, closeKernel);
        whiteTextOnBlack.Dispose();

        // 得到的是白字黑底，翻转成 OCR 要的黑字白底。
        var blackTextOnWhite = new Mat();
        Cv2.BitwiseNot(closed, blackTextOnWhite);
        closed.Dispose();

        return blackTextOnWhite;
    }
}
