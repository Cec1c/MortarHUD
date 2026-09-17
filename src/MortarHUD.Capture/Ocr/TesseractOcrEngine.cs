using System.Diagnostics;
using MortarHUD.Core.Configuration;
using MortarHUD.Core.Models;
using OpenCvSharp;
using Tesseract;

namespace MortarHUD.Capture.Ocr;

/// <summary>
/// Tesseract 5 引擎（TDD §6 的首选候选）。
/// </summary>
/// <remarks>
/// <para>
/// <see cref="TesseractEngine"/> 不是线程安全的，而且构造一次要上百毫秒，
/// 所以这里保持一个长生命周期实例，用锁串行化调用。
/// 热键触发本来就是一秒钟几次的量级，串行完全够用。
/// </para>
/// <para>
/// 字符白名单是本引擎最大的杠杆：把候选集从「全部 Unicode」收窄到
/// <c>0123456789xy.:-</c> 之后，形近字的误判会大幅减少。
/// </para>
/// </remarks>
public sealed class TesseractOcrEngine : ICoordinateOcrEngine
{
    private readonly OcrSettings _settings;
    private readonly object _sync = new();

    private TesseractEngine? _engine;
    private bool _disposed;
    private string? _initializationError;

    public TesseractOcrEngine(OcrSettings? settings = null, string? tessdataPath = null)
    {
        _settings = settings ?? new OcrSettings();
        TessdataPath = string.IsNullOrWhiteSpace(tessdataPath)
            ? TessdataLocator.Resolve(_settings.TessdataPath)
            : tessdataPath;
    }

    public string Name => "Tesseract";

    public string TessdataPath { get; }

    /// <summary>语言包是否就绪。UI 用它决定要不要禁用这个引擎。</summary>
    public bool IsAvailable => InitializationError is null && EnsureEngine() is not null;

    public string? InitializationError => _initializationError ??= Probe();

    /// <summary>
    /// 只检查语言包在不在，不构造引擎。
    /// 构造引擎要上百毫秒，设置页刷新时不该付这个代价。
    /// </summary>
    private string? Probe()
    {
        if (!Directory.Exists(TessdataPath))
        {
            return $"tessdata 目录不存在：{TessdataPath}";
        }

        var languageFile = Path.Combine(TessdataPath, _settings.Language + ".traineddata");
        if (!File.Exists(languageFile))
        {
            return $"缺少语言包：{languageFile}";
        }

        return null;
    }

    public Task<CoordinateOcrResult> RecognizeAsync(Mat input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        ObjectDisposedException.ThrowIf(_disposed, this);

        return Task.Run(() => Recognize(input, cancellationToken), cancellationToken);
    }

    private CoordinateOcrResult Recognize(Mat input, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        if (input.Empty())
        {
            return CoordinateOcrResult.Failed("EMPTY_IMAGE", ocrTime: stopwatch.Elapsed);
        }

        cancellationToken.ThrowIfCancellationRequested();

        lock (_sync)
        {
            var engine = EnsureEngine();
            if (engine is null)
            {
                return CoordinateOcrResult.Failed(
                    _initializationError ?? "TESSERACT_UNAVAILABLE", ocrTime: stopwatch.Elapsed);
            }

            try
            {
                // Mat → PNG → Pix。走内存编码可以避开 System.Drawing。
                Cv2.ImEncode(".png", input, out var encoded);

                using var pix = Pix.LoadFromMemory(encoded);
                using var page = engine.Process(pix, MapPageSegMode(_settings.PageSegMode));

                var text = page.GetText() ?? "";
                var confidence = ReadBestWordConfidence(page);

                stopwatch.Stop();

                return new CoordinateOcrResult
                {
                    // 引擎只对「读出了东西」负责，数值可信度交给 Parser + Validator。
                    Success = !string.IsNullOrWhiteSpace(text),
                    RawText = text,
                    Confidence = Math.Clamp(confidence, 0.0, 1.0),
                    OcrTime = stopwatch.Elapsed,
                    Error = string.IsNullOrWhiteSpace(text) ? "NO_TEXT_RECOGNIZED" : null,
                };
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                stopwatch.Stop();
                return CoordinateOcrResult.Failed($"TESSERACT_ERROR: {ex.Message}", ocrTime: stopwatch.Elapsed);
            }
        }
    }

    private TesseractEngine? EnsureEngine()
    {
        if (_disposed)
        {
            return null;
        }

        if (_engine is not null)
        {
            return _engine;
        }

        if (_initializationError is not null)
        {
            return null;
        }

        try
        {
            var engine = new TesseractEngine(TessdataPath, _settings.Language, EngineMode.Default);

            if (!string.IsNullOrWhiteSpace(_settings.CharacterWhitelist))
            {
                engine.SetVariable("tessedit_char_whitelist", _settings.CharacterWhitelist);
            }

            // 关掉 Tesseract 自带的字典纠错：它会把 x107 之类「不像单词」的串改坏。
            engine.SetVariable("load_system_dawg", "0");
            engine.SetVariable("load_freq_dawg", "0");

            _engine = engine;
            return _engine;
        }
        catch (Exception ex)
        {
            _initializationError =
                $"Tesseract 初始化失败：{ex.Message}（tessdata 路径：{TessdataPath}）";
            return null;
        }
    }

    /// <summary>
    /// 取「最可信的那个词」的置信度，而不是全图平均。
    /// </summary>
    /// <remarks>
    /// 实测全图平均置信度在这个场景里几乎没有判别力：ROI 里除了坐标还有地图纹理、
    /// 网格线、标记图标，Tesseract 会把它们切成若干噪声词，平均下来只有 0.00~0.11，
    /// 而真正读对的坐标本身置信度要高得多。用最高词置信度才能反映
    /// 「那串数字读得清不清楚」。
    /// </remarks>
    private static double ReadBestWordConfidence(Page page)
    {
        var best = 0.0;

        try
        {
            using var iterator = page.GetIterator();
            iterator.Begin();

            do
            {
                // Tesseract 的词置信度是 0~100 的百分数，统一折算成 0~1。
                var wordConfidence = iterator.GetConfidence(PageIteratorLevel.Word) / 100.0;
                if (double.IsFinite(wordConfidence) && wordConfidence > best)
                {
                    best = wordConfidence;
                }
            }
            while (iterator.Next(PageIteratorLevel.Word));
        }
        catch (Exception ex)
        {
            // 拿不到词级置信度不该让整次识别失败，退回平均置信度。
            System.Diagnostics.Debug.WriteLine($"读取词级置信度失败，退回平均值：{ex.Message}");
            var mean = page.GetMeanConfidence() / 100.0;
            return double.IsFinite(mean) ? Math.Clamp(mean, 0.0, 1.0) : 0.0;
        }

        return best;
    }

    private static PageSegMode MapPageSegMode(int value) => value switch
    {
        3 => PageSegMode.Auto,
        6 => PageSegMode.SingleBlock,
        7 => PageSegMode.SingleLine,
        11 => PageSegMode.SparseText,
        13 => PageSegMode.RawLine,
        _ => PageSegMode.SingleBlock,
    };

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        lock (_sync)
        {
            _engine?.Dispose();
            _engine = null;
        }

        GC.SuppressFinalize(this);
    }
}
