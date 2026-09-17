using OpenCvSharp;

namespace MortarHUD.Capture.ImageProcessing;

/// <summary>
/// 把原始 ROI 处理成 OCR 友好的二值图（TDD §14）。
/// </summary>
/// <remarks>
/// 输出约定：<strong>黑字白底</strong>，单通道 8 位。
/// Tesseract 是在「深色文字 + 浅色纸张」上训练的，这个方向识别率最高。
/// </remarks>
public interface IImagePreprocessor
{
    /// <summary>流水线名字，用于设置页与 Benchmark 报告。</summary>
    string Name { get; }

    /// <summary>
    /// 处理一张 ROI。
    /// </summary>
    /// <param name="input">原始 BGR 或灰度图。</param>
    /// <returns>新建的二值图，调用方负责释放。</returns>
    Mat Process(Mat input);
}

/// <summary>预处理过程中的错误。</summary>
public sealed class ImagePreprocessingException : Exception
{
    public ImagePreprocessingException(string message) : base(message)
    {
    }

    public ImagePreprocessingException(string message, Exception inner) : base(message, inner)
    {
    }
}
