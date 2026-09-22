using OpenCvSharp;

namespace MortarHUD.Capture.ImageProcessing;

/// <summary>
/// Pipeline C（TDD §14）：亮色 UI 文本提取 → 形态学顶帽 → 双门限 → 阈值。
/// </summary>
/// <remarks>
/// <para>
/// 游戏坐标是「白色笔画 + 深色描边」，地图背景则是缓变的灰阶。
/// 白顶帽（原图 − 开运算）只留下「比周围亮、且比结构元窄」的结构，
/// 大块亮区域（水泥地、雪地、UI 面板）会被开运算吃掉。
/// </para>
/// <para>
/// <strong>为什么要第二个门限：</strong>实测 fixture 003 里，地图上一条浅色小路
/// 也满足「比周围亮、且足够窄」，顶帽会把它整条抓进来，还会和旁边的数字粘成一团，
/// 导致 Tesseract 整行读废。再叠一个「突出度幅度」门限就能把路滤掉——
/// 详见 <see cref="PreprocessorBase.BuildStrengthMask"/> 里关于门限该作用在
/// 灰度域还是顶帽域的说明。
/// </para>
/// </remarks>
public sealed class PipelineC : PreprocessorBase
{
    private readonly bool _neutralText;
    private readonly double _neutralWeight;
    public PipelineC(bool neutralText = true, double neutralWeight = 1)
    {
        _neutralText = neutralText;
        _neutralWeight = neutralWeight;
    }

    protected override Mat PrepareGray(Mat input)
    {
        if (!_neutralText || input.Channels() < 3) return ToGray(input);
        // 坐标是白色，三通道都亮；地图的青蓝边框只有部分通道亮。
        // 普通灰度保留了边框，令 PSM 6 把 y110.38 吞成 y0.38。
        // 纯最小值与保留半份灰度的版本对细笔画有不同误差，Auto 同时校验两者。
        // 2026-09-22 的 132 帧回放：两者均通过 42（原 22），无旧通过帧退化。
        // 不放宽解析或投票规则，仍然拒绝任何有效读数之间的分歧。
        var channels = Cv2.Split(input);
        try
        {
            var gray = new Mat();
            Cv2.Min(channels[0], channels[1], gray);
            Cv2.Min(gray, channels[2], gray);
            using var luminance = ToGray(input);
            Cv2.AddWeighted(gray, _neutralWeight, luminance, 1 - _neutralWeight, 0, gray);
            return gray;
        }
        finally { foreach (var channel in channels) channel.Dispose(); }
    }
    /// <summary>
    /// 结构元尺寸。必须在放大后的尺度上明显大于笔画宽度，
    /// 否则文字本身会被当作「大块亮区域」而被开运算削掉。
    /// </summary>
    private const int KernelSize = 25;

    /// <summary>突出度门限取顶帽高分位数的比例。</summary>
    private const double StrengthGateRatio = 0.60;

    /// <summary>用来估计「文字有多突出」的分位数。</summary>
    private const double StrengthReferencePercentile = 99.0;

    public override string Name => _neutralText && _neutralWeight < 1 ? "C-Balanced" : "C";

    protected override Mat Binarize(Mat upscaledGray)
    {
        using var kernel = Cv2.GetStructuringElement(
            MorphShapes.Rect, new Size(KernelSize, KernelSize));

        var topHat = new Mat();
        Cv2.MorphologyEx(upscaledGray, topHat, MorphTypes.TopHat, kernel);

        // 门限 1：局部突出度。顶帽结果里背景接近 0、笔画接近原始亮度差。
        using var localMask = new Mat();
        Cv2.Threshold(topHat, localMask, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);

        // 门限 2：突出度幅度。挡掉「比周围亮、但亮得不够」的地图干扰物。
        using var strengthMask = BuildStrengthMask(
            topHat, StrengthReferencePercentile, StrengthGateRatio);

        var whiteTextOnBlack = new Mat();
        Cv2.BitwiseAnd(localMask, strengthMask, whiteTextOnBlack);
        topHat.Dispose();

        // 顶帽 + 双门限得到的是「白字黑底」，翻转成 OCR 要的黑字白底。
        using var blackTextOnWhite = new Mat();
        Cv2.BitwiseNot(whiteTextOnBlack, blackTextOnWhite);
        whiteTextOnBlack.Dispose();

        // 顶帽对「细长结构」同样敏感，地图网格线也会被它抓进来，
        // 所以这里和 Pipeline A 一样要去直线。详见 RemoveLongLines。
        return RemoveLongLines(blackTextOnWhite, LineKernelLength(blackTextOnWhite));
    }
}
