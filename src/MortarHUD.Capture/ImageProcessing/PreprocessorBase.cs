using OpenCvSharp;

namespace MortarHUD.Capture.ImageProcessing;

/// <summary>三条流水线共用的步骤。</summary>
public abstract class PreprocessorBase : IImagePreprocessor
{
    /// <summary>
    /// 放大倍数。游戏坐标的字符高度只有约 11px，
    /// Tesseract 在这个尺寸上几乎读不出东西，放大到 3~4 倍后识别率会有质的变化。
    /// </summary>
    protected const double UpscaleFactor = 3.0;

    /// <summary>二值图四周留的白边，Tesseract 需要安静区才能正确切分字符。</summary>
    protected const int QuietZonePixels = 24;

    public abstract string Name { get; }

    /// <summary>本流水线使用的放大倍数，Pipeline B 用 4 倍。</summary>
    protected virtual double Scale => UpscaleFactor;

    public Mat Process(Mat input)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (input.Empty())
        {
            throw new ImagePreprocessingException("ROI 为空，没有可处理的像素。");
        }

        using var gray = ToGray(input);
        using var upscaled = Upscale(gray, Scale);

        // 约定：Binarize 直接产出「黑字白底」——Tesseract 训练时见的就是这个极性。
        // 上游不要再去反转它，否则会得到白字黑底，识别率明显下降。
        using var binary = Binarize(upscaled);

        return AddQuietZone(binary);
    }

    /// <summary>核心二值化步骤，由各流水线实现。必须输出「黑字白底」。</summary>
    protected abstract Mat Binarize(Mat upscaledGray);

    protected static Mat ToGray(Mat input)
    {
        var gray = new Mat();

        if (input.Channels() == 1)
        {
            input.CopyTo(gray);
            return gray;
        }

        var code = input.Channels() == 4
            ? ColorConversionCodes.BGRA2GRAY
            : ColorConversionCodes.BGR2GRAY;

        Cv2.CvtColor(input, gray, code);
        return gray;
    }

    protected static Mat Upscale(Mat gray, double scale)
    {
        var result = new Mat();

        var width = Math.Max(1, (int)Math.Round(gray.Width * scale));
        var height = Math.Max(1, (int)Math.Round(gray.Height * scale));

        Cv2.Resize(gray, result, new Size(width, height), 0, 0, InterpolationFlags.Cubic);
        return result;
    }

    /// <summary>四周补白边，给 Tesseract 留出安静区。</summary>
    protected static Mat AddQuietZone(Mat blackTextOnWhite)
    {
        var padded = new Mat();

        Cv2.CopyMakeBorder(
            blackTextOnWhite,
            padded,
            QuietZonePixels,
            QuietZonePixels,
            QuietZonePixels,
            QuietZonePixels,
            BorderTypes.Constant,
            new Scalar(255));

        return padded;
    }

    /// <summary>
    /// 生成「突出度」掩码：只保留局部对比度足够强的像素。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 门限作用在<em>顶帽幅度</em>上而不是灰度上，这一点是 Benchmark 逼出来的：
    /// 起初用的是「灰度的 0.82 × p99」，结果 fixture 002 直接判失败——
    /// 那里的坐标压在亮色背景上，笔画灰度只有 200~235，被门限削得七零八落，
    /// 末位数字直接断开。
    /// </para>
    /// <para>
    /// 换到顶帽域就没有这个问题：顶帽衡量的是「比周围亮多少」，
    /// 压在亮背景上的文字只是灰度低，突出度依然很高。
    /// 实测（原始尺度 9x9 核）：
    /// 文字区顶帽 P99 = 200~225，而水泥墙 / 浅色小路等干扰物只有 104~141，
    /// 两者之间有清晰的分界。
    /// </para>
    /// <para>
    /// 门限取顶帽 p99 的相对比例而不是定值，这样换地图、换分辨率都不用重调。
    /// 用分位数而不是最大值，是为了避免单个过曝像素把门限整个抬起来。
    /// </para>
    /// </remarks>
    protected static Mat BuildStrengthMask(
        Mat topHat, double referencePercentile = 99.0, double ratio = 0.60)
    {
        var gate = topHat.Rows * (double)topHat.Cols * referencePercentile / 100.0;

        using var histogram = new Mat();
        Cv2.CalcHist([topHat], [0], null, histogram, 1, [256], [new Rangef(0, 256)]);

        var cumulative = 0.0;
        var threshold = 0.0;

        for (var level = 0; level < 256; level++)
        {
            cumulative += histogram.At<float>(level);
            if (cumulative >= gate)
            {
                threshold = level * ratio;
                break;
            }
        }

        var mask = new Mat();
        Cv2.Threshold(topHat, mask, threshold, 255, ThresholdTypes.Binary);
        return mask;
    }

    /// <summary>把图像按标准差拉满对比度，用于低对比度的地图底色。</summary>
    protected static void StretchContrast(Mat gray)
    {
        Cv2.MinMaxLoc(gray, out double min, out double max);

        if (max - min < 1)
        {
            return;
        }

        var alpha = 255.0 / (max - min);
        var beta = -min * alpha;
        gray.ConvertTo(gray, MatType.CV_8UC1, alpha, beta);
    }
}
