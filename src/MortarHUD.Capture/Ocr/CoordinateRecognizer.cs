using System.Diagnostics;
using MortarHUD.Capture.ImageProcessing;
using MortarHUD.Core.Models;
using MortarHUD.Core.Parsing;
using MortarHUD.Core.Validation;
using OpenCvSharp;

namespace MortarHUD.Capture.Ocr;

/// <summary>
/// 识别编排：预处理 → OCR → 解析 → 校验，并在多条流水线之间做交叉验证。
/// </summary>
/// <remarks>
/// <para>
/// 交叉验证是这里最重要的安全机制（TDD §14 的 Auto 模式、§16 的不许静默用错数据）。
/// 同一个 ROI 用不同算法二值化后会得到不同的图，各自独立识别；
/// 如果它们给出<strong>互相矛盾</strong>的坐标，说明至少有一条被噪声带偏了，
/// 此时宁可报 OCR FAILED 让用户重按一次，也不能挑一个看着顺眼的用下去——
/// 那正是「静默使用错误数据」。
/// </para>
/// <para>
/// 只有一条流水线成功时不构成矛盾，正常采纳：不同地图区域本来就适合不同算法。
/// </para>
/// </remarks>
public sealed class CoordinateRecognizer
{
    /// <summary>判定「两条流水线给出同一个坐标」时允许的差值。</summary>
    private const double AgreementTolerance = 0.005;

    private readonly IReadOnlyList<ICoordinateOcrEngine> _engines;
    private readonly IReadOnlyList<IImagePreprocessor> _preprocessors;
    private readonly ICoordinateTextParser _parser;
    private readonly ICoordinateValidator _validator;
    private readonly IRecognitionObserver? _observer;

    public CoordinateRecognizer(
        IReadOnlyList<ICoordinateOcrEngine> engines,
        IReadOnlyList<IImagePreprocessor> preprocessors,
        ICoordinateTextParser parser,
        ICoordinateValidator validator,
        IRecognitionObserver? observer = null)
    {
        _observer = observer;
        if (engines.Count == 0)
        {
            throw new ArgumentException("至少要有一个可用的 OCR 引擎。", nameof(engines));
        }

        if (preprocessors.Count == 0)
        {
            throw new ArgumentException("至少要有一条预处理流水线。", nameof(preprocessors));
        }

        _engines = engines;
        _preprocessors = preprocessors;
        _parser = parser;
        _validator = validator;
    }

    public async Task<RecognitionOutcome> RecognizeAsync(Mat roi, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(roi);

        var totalWatch = Stopwatch.StartNew();
        var attempts = new List<RecognitionAttempt>();

        _observer?.OnRawRoi(roi);

        foreach (var engine in _engines)
        {
            foreach (var preprocessor in _preprocessors)
            {
                cancellationToken.ThrowIfCancellationRequested();

                attempts.Add(await RunOneAsync(engine, preprocessor, roi, cancellationToken)
                    .ConfigureAwait(false));
            }
        }

        totalWatch.Stop();
        return Aggregate(attempts, totalWatch.Elapsed);
    }

    private async Task<RecognitionAttempt> RunOneAsync(
        ICoordinateOcrEngine engine,
        IImagePreprocessor preprocessor,
        Mat roi,
        CancellationToken cancellationToken)
    {
        var preprocessWatch = Stopwatch.StartNew();
        Mat processed;

        try
        {
            processed = preprocessor.Process(roi);
        }
        catch (Exception ex)
        {
            return new RecognitionAttempt
            {
                Engine = engine.Name,
                Pipeline = preprocessor.Name,
                Error = $"PREPROCESS_FAILED: {ex.Message}",
                PreprocessTime = preprocessWatch.Elapsed,
            };
        }

        preprocessWatch.Stop();

        try
        {
            using (processed)
            {
                _observer?.OnProcessed(engine.Name, preprocessor.Name, processed);

                var ocrResult = await engine.RecognizeAsync(processed, cancellationToken)
                    .ConfigureAwait(false);

                var parseResult = _parser.Parse(ocrResult.RawText);

                var attempt = new RecognitionAttempt
                {
                    Engine = engine.Name,
                    Pipeline = preprocessor.Name,
                    ProcessedWidth = processed.Width,
                    ProcessedHeight = processed.Height,
                    RawText = ocrResult.RawText,
                    RepairedText = parseResult.RepairedText,
                    Confidence = ocrResult.Confidence,
                    PreprocessTime = preprocessWatch.Elapsed,
                    OcrTime = ocrResult.OcrTime,
                };

                if (!parseResult.Success)
                {
                    return attempt with { Error = parseResult.Error ?? "PARSE_FAILED" };
                }

                var validation = _validator.Validate(ocrResult with
                {
                    Success = true,
                    X = parseResult.X,
                    Y = parseResult.Y,
                });

                if (!validation.IsValid || validation.Coordinate is null)
                {
                    return attempt with { Error = validation.Error ?? "VALIDATION_FAILED" };
                }

                return attempt with { Coordinate = validation.Coordinate };
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new RecognitionAttempt
            {
                Engine = engine.Name,
                Pipeline = preprocessor.Name,
                Error = $"OCR_FAILED: {ex.Message}",
                PreprocessTime = preprocessWatch.Elapsed,
            };
        }
    }

    /// <summary>
    /// 汇总全部尝试。规则：多数一致则采纳；出现不可调和的矛盾则整体判失败。
    /// </summary>
    private static RecognitionOutcome Aggregate(List<RecognitionAttempt> attempts, TimeSpan totalTime)
    {
        var successes = attempts.Where(a => a.Success).ToList();

        if (successes.Count == 0)
        {
            return RecognitionOutcome.Failed(DiagnoseFailure(attempts), attempts, totalTime);
        }

        var groups = GroupByCoordinate(successes);

        var best = groups.OrderByDescending(g => g.Count).First();

        // 并列第一 = 没有任何一个答案得到多数支持，无法判断谁对。
        var tied = groups.Count(g => g.Count == best.Count) > 1;
        if (tied)
        {
            return RecognitionOutcome.Failed("PIPELINE_DISAGREEMENT", attempts, totalTime);
        }

        var winner = best[0];

        return new RecognitionOutcome
        {
            Success = true,
            Coordinate = winner.Coordinate,
            RawText = winner.RawText,
            Confidence = ComputeConfidence(best.Max(a => a.Confidence), best.Count, attempts.Count),
            Attempts = attempts,
            TotalTime = totalTime,
        };
    }

    /// <summary>
    /// 判断这次失败到底属于哪一类。
    /// </summary>
    /// <remarks>
    /// 之前直接返回第一个非空错误，结果是「ROI 里压根没有文字」和
    /// 「读到了文字但格式不对」都显示成同一条 X_NOT_FOUND——
    /// 而这两者的排查方向完全相反：前者要挪 ROI，后者要调解析或预处理。
    /// 这里把它们分开，日志里一眼就能看出该往哪查。
    /// </remarks>
    private static string DiagnoseFailure(List<RecognitionAttempt> attempts)
    {
        // 引擎侧就没出文本：ROI 多半没框住坐标读数。
        var producedText = attempts.Any(a =>
            !string.IsNullOrEmpty(a.RawText) && !string.IsNullOrWhiteSpace(a.RawText));

        if (!producedText)
        {
            var engineError = attempts
                .Select(a => a.Error)
                .FirstOrDefault(e => !string.IsNullOrWhiteSpace(e));

            return engineError is not null && engineError.StartsWith("PREPROCESS", StringComparison.Ordinal)
                ? engineError
                : "NO_TEXT_IN_ROI";
        }

        // 有文本但解析不过：把出现最多的那个解析错误报出来。
        var parseErrors = attempts
            .Where(a => !a.Success && !string.IsNullOrWhiteSpace(a.Error))
            .Select(a => a.Error!)
            .GroupBy(e => e)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .ToList();

        return parseErrors.Count > 0
            ? $"PARSE_FAILED: {string.Join(" / ", parseErrors)}"
            : "OCR_FAILED";
    }

    /// <summary>
    /// 把每条流水线的原始输出整理成一段可读文本，供日志与 Debug 面板使用。
    /// </summary>
    /// <remarks>
    /// 排查 OCR 问题时，光知道「X_NOT_FOUND」没用——必须看到引擎到底吐了什么。
    /// </remarks>
    public static string DescribeAttempts(IEnumerable<RecognitionAttempt> attempts)
    {
        var lines = new List<string>();

        foreach (var attempt in attempts)
        {
            var outcome = attempt.Success
                ? $"OK {attempt.Coordinate!.Value.X:0.00}/{attempt.Coordinate.Value.Y:0.00}"
                : attempt.Error ?? "FAIL";

            var raw = string.IsNullOrWhiteSpace(attempt.RawText)
                ? "(空)"
                : attempt.RawText.Replace('\r', ' ').Replace('\n', '|').Trim();

            lines.Add($"{attempt.Engine}/{attempt.Pipeline} {outcome} raw=[{raw}]");
        }

        return string.Join(" ; ", lines);
    }

    /// <summary>
    /// 计算最终置信度。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 不能直接用 OCR 引擎自己的置信度：Benchmark 实测下来，
    /// Tesseract 对<em>读对了</em>的坐标也会给出 0.00（fixture 003 就是），
    /// 直接拿它当门槛会把正确结果拦掉。
    /// </para>
    /// <para>
    /// 真正有判别力的证据是「多条独立流水线是否得到同一个答案」——
    /// 它们用不同的二值化算法，错法各不相同，能同时撞到同一个值就很难是巧合。
    /// OCR 自身的词置信度只作为辅助加权。
    /// </para>
    /// <para>
    /// 基线 0.55 表示「格式解析通过且数值在配置范围内」——
    /// 这本身已经排除了绝大多数脏数据，但仍然留出了被扣分的空间，
    /// 所以它不是「一定可信」，用户调高 MinimumConfidence 依然能更保守。
    /// </para>
    /// </remarks>
    private static double ComputeConfidence(double ocrConfidence, int agreeingCount, int attemptCount)
    {
        const double baseline = 0.55;
        const double agreementWeight = 0.25;
        const double ocrWeight = 0.20;

        var agreementRatio = attemptCount <= 0 ? 0.0 : (double)agreeingCount / attemptCount;

        return Math.Clamp(
            baseline + agreementWeight * agreementRatio + ocrWeight * Math.Clamp(ocrConfidence, 0.0, 1.0),
            0.0,
            1.0);
    }

    private static List<List<RecognitionAttempt>> GroupByCoordinate(List<RecognitionAttempt> successes)
    {
        var groups = new List<List<RecognitionAttempt>>();

        foreach (var attempt in successes)
        {
            var coordinate = attempt.Coordinate!.Value;

            var target = groups.FirstOrDefault(g =>
                Math.Abs(g[0].Coordinate!.Value.X - coordinate.X) < AgreementTolerance
                && Math.Abs(g[0].Coordinate!.Value.Y - coordinate.Y) < AgreementTolerance);

            if (target is null)
            {
                groups.Add([attempt]);
            }
            else
            {
                target.Add(attempt);
            }
        }

        return groups;
    }
}
