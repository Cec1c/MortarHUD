using System.Diagnostics;
using System.Globalization;
using System.Text;
using MortarHUD.Benchmark;
using MortarHUD.Capture.ImageProcessing;
using MortarHUD.Capture.Ocr;
using MortarHUD.Capture.ScreenCapture;
using MortarHUD.Core.Configuration;
using MortarHUD.Core.Parsing;
using MortarHUD.Core.Validation;
using OpenCvSharp;

Console.OutputEncoding = Encoding.UTF8;

// ================= 参数 =================

var repoRoot = LocateRepoRoot();
var fixturesPath = Path.Combine(repoRoot, "tests", "Fixtures", "fixtures.json");
var screenshotDirectory = Path.Combine(repoRoot, "tests", "Fixtures", "screenshots");
var tessdataPath = Path.Combine(repoRoot, "src", "MortarHUD.App", "Models", "tessdata");

if (!File.Exists(fixturesPath))
{
    Console.Error.WriteLine($"找不到 fixture 清单：{fixturesPath}");
    return 2;
}

var manifest = FixtureManifest.Load(fixturesPath);

// 可选：从 fixture 重新学习字形模板库。
// 必须跑在创建任何引擎之前，否则 TemplateOcrEngine 会加载到旧的模板。
var genIndex = Array.IndexOf(args, "--gen-templates");
if (genIndex >= 0)
{
    var outputDirectory = genIndex + 1 < args.Length
        ? args[genIndex + 1]
        : Path.Combine(repoRoot, "src", "MortarHUD.App", "Models", "glyphs");

    Console.WriteLine("正在从 fixture 生成字形模板库…");
    var generated = TemplateGenerator.Generate(
        manifest, screenshotDirectory, outputDirectory, PreprocessorFactory.All);

    TemplateGenerator.WriteContactSheet(
        generated, Path.Combine(outputDirectory, "contact-sheet.png"));

    Console.WriteLine();
}

// 可选：把预处理结果 dump 出来，肉眼确认二值化干不干净。
// 调 OCR 的时候这是唯一靠谱的排查手段——只看识别结果猜不出图长什么样。
var dumpIndex = Array.IndexOf(args, "--dump");
var dumpDirectory = dumpIndex >= 0 && dumpIndex + 1 < args.Length ? args[dumpIndex + 1] : null;

if (dumpDirectory is not null)
{
    Directory.CreateDirectory(dumpDirectory);
}

Console.WriteLine("MortarHUD OCR Benchmark");
Console.WriteLine(new string('=', 108));
Console.WriteLine($"Fixture 数量   : {manifest.Fixtures.Count}");
Console.WriteLine($"参考分辨率     : {manifest.ReferenceResolution.Width}x{manifest.ReferenceResolution.Height}");
Console.WriteLine($"ROI 默认值     : offset({manifest.RoiDefaults.OffsetX},{manifest.RoiDefaults.OffsetY}) "
                  + $"{manifest.RoiDefaults.Width}x{manifest.RoiDefaults.Height}");
Console.WriteLine($"tessdata       : {(TessdataLocator.IsUsable(tessdataPath) ? tessdataPath : "未找到")}");

if (dumpDirectory is not null)
{
    Console.WriteLine($"预处理图转储   : {dumpDirectory}");
}

Console.WriteLine();

// ================= 共用组件 =================

var parser = new CoordinateTextParser(new CoordinateTextParserOptions { RequireDecimalPoint = true });
var validator = new CoordinateValidator(new CoordinateValidationOptions
{
    CoordinateMin = 0,
    CoordinateMax = 200,

    // Benchmark 阶段不因置信度淘汰，先把真实识别率量出来；
    // 置信度分布会单独打印，供确定 MinimumConfidence 的默认值。
    MinimumConfidence = 0.0,
});

var ocrSettings = new OcrSettings { TessdataPath = tessdataPath };

// ================= 待测配置 =================

// 每条配置 = 一组「引擎 × 流水线」。Auto 配置会把三条流水线一起跑并做交叉验证。
var configurations = new List<BenchmarkConfiguration>
{
    new("Auto", OcrEngineFactory.Resolve(OcrEngineNames.Auto, ocrSettings, tessdataPath),
        PreprocessorFactory.AutoCandidates),
};

foreach (var engine in OcrEngineFactory.CreateAllForBenchmark(ocrSettings, tessdataPath))
{
    if (engine is TesseractOcrEngine { IsAvailable: false } broken)
    {
        Console.Error.WriteLine($"跳过 {broken.Name}：{broken.InitializationError}");
        engine.Dispose();
        continue;
    }

    foreach (var preprocessor in PreprocessorFactory.All)
    {
        configurations.Add(new BenchmarkConfiguration(
            $"{engine.Name}+{preprocessor.Name}",
            [engine],
            [preprocessor]));
    }

    configurations.Add(new BenchmarkConfiguration($"{engine.Name}+All", [engine], PreprocessorFactory.All));
}

// ================= 跑矩阵 =================

var enginePool = configurations
    .SelectMany(c => c.Engines)
    .Distinct()
    .ToList();

var rows = new List<BenchmarkRow>();

foreach (var configuration in configurations)
{
    foreach (var fixture in manifest.Fixtures)
    {
        rows.Add(RunOne(configuration, fixture, screenshotDirectory, parser, validator, dumpDirectory));
    }
}

foreach (var engine in enginePool)
{
    engine.Dispose();
}

// ================= 报告 =================

PrintDetail(rows);
PrintSummary(rows);
PrintConfidenceDistribution(rows);
PrintRecommendation(rows);

// TDD §17 要求产出 Benchmark 报告。除了打到控制台，也落一份 Markdown，
// 这样「当前默认引擎为什么是它」这件事在仓库里有据可查，
// 而不是只存在于某次终端输出里。
var reportPath = Path.Combine(repoRoot, "docs", "ocr-benchmark.md");
Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
File.WriteAllText(reportPath, BuildMarkdownReport(manifest, rows), Encoding.UTF8);

Console.WriteLine($"报告已写入：{reportPath}");

return 0;

// ================= 实现 =================

static BenchmarkRow RunOne(
    BenchmarkConfiguration configuration,
    FixtureCase fixture,
    string screenshotDirectory,
    ICoordinateTextParser parser,
    ICoordinateValidator validator,
    string? dumpDirectory)
{
    var row = new BenchmarkRow
    {
        Fixture = fixture.Id,
        Configuration = configuration.Name,
        ExpectedX = fixture.ExpectedX,
        ExpectedY = fixture.ExpectedY,
    };

    var screenshotPath = Path.Combine(screenshotDirectory, fixture.Screenshot);
    if (!File.Exists(screenshotPath))
    {
        row.Error = "缺少截图";
        return row;
    }

    var total = Stopwatch.StartNew();

    try
    {
        using var full = Cv2.ImRead(screenshotPath, ImreadModes.Color);
        if (full.Empty())
        {
            row.Error = "截图读取失败";
            return row;
        }

        // 用与运行时完全相同的 ROI 逻辑裁切，保证 Benchmark 量的是线上路径。
        var roiRect = RoiCalculator.ClampTo(
            new System.Drawing.Rectangle(fixture.RoiX, fixture.RoiY, fixture.RoiWidth, fixture.RoiHeight),
            new System.Drawing.Rectangle(0, 0, full.Width, full.Height));

        using var roi = new Mat(full, new Rect(roiRect.X, roiRect.Y, roiRect.Width, roiRect.Height));

        if (dumpDirectory is not null)
        {
            Cv2.ImWrite(Path.Combine(dumpDirectory, $"RAW_{fixture.Id}.png"), roi);
            DumpProcessed(configuration, fixture, roi, dumpDirectory);
        }

        var recognizer = new CoordinateRecognizer(
            configuration.Engines, configuration.Preprocessors, parser, validator);

        var outcome = recognizer.RecognizeAsync(roi, CancellationToken.None).GetAwaiter().GetResult();

        row.AttemptSummary = string.Join(" ; ", outcome.Attempts.Select(DescribeAttempt));
        row.AttemptCount = outcome.Attempts.Count;
        row.SuccessCount = outcome.Attempts.Count(a => a.Success);

        if (!outcome.Success || outcome.Coordinate is null)
        {
            row.Error = outcome.Error;
            row.TotalMs = total.Elapsed.TotalMilliseconds;
            return row;
        }

        row.ParsedX = outcome.Coordinate.Value.X;
        row.ParsedY = outcome.Coordinate.Value.Y;
        row.Confidence = outcome.Confidence;

        var rawText = outcome.Attempts.FirstOrDefault(a => a.Success)?.RawText ?? "";
        row.RawText = rawText.Replace("\r", "").Replace("\n", " | ").Trim();

        row.Correct = Math.Abs(row.ParsedX.Value - fixture.ExpectedX) < 0.005
                      && Math.Abs(row.ParsedY.Value - fixture.ExpectedY) < 0.005;

        row.TotalMs = total.Elapsed.TotalMilliseconds;
        return row;
    }
    catch (Exception ex)
    {
        row.Error = ex.GetType().Name + ": " + ex.Message;
        row.TotalMs = total.Elapsed.TotalMilliseconds;
        return row;
    }
}

/// <summary>把一次尝试压成一行，失败时把原始 OCR 文本也带上——排查时最需要它。</summary>
static string DescribeAttempt(RecognitionAttempt attempt)
{
    if (attempt.Success)
    {
        return $"{attempt.Pipeline}:OK";
    }

    var raw = Truncate(attempt.RawText, 22);
    return $"{attempt.Pipeline}:{attempt.Error}[\"{raw}\"]";
}

static void DumpProcessed(
    BenchmarkConfiguration configuration, FixtureCase fixture, Mat roi, string dumpDirectory)
{
    foreach (var preprocessor in configuration.Preprocessors)
    {
        try
        {
            using var processed = preprocessor.Process(roi);
            Cv2.ImWrite(
                Path.Combine(dumpDirectory, $"{Sanitize(configuration.Name)}_{preprocessor.Name}_{fixture.Id}.png"),
                processed);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"转储 {configuration.Name}/{preprocessor.Name} 失败：{ex.Message}");
        }
    }
}

static string Sanitize(string name) => name.Replace('+', '_');

static void PrintDetail(List<BenchmarkRow> rows)
{
    Console.WriteLine("明细");
    Console.WriteLine(new string('-', 108));
    Console.WriteLine(
        $"{"Fixture",-8} {"配置",-16} {"结果",-6} {"X",-9} {"Y",-9} {"期望X",-9} {"期望Y",-9} "
        + $"{"Total",-9} {"Conf",-6} 说明");

    foreach (var row in rows)
    {
        var verdict = row.Error is null ? (row.Correct ? "PASS" : "FAIL") : "ERR";

        Console.WriteLine(
            $"{row.Fixture,-8} {row.Configuration,-16} {verdict,-6} "
            + $"{Format(row.ParsedX),-9} {Format(row.ParsedY),-9} "
            + $"{row.ExpectedX.ToString("0.00", CultureInfo.InvariantCulture),-9} "
            + $"{row.ExpectedY.ToString("0.00", CultureInfo.InvariantCulture),-9} "
            + $"{row.TotalMs.ToString("0.0", CultureInfo.InvariantCulture) + "ms",-9} "
            + $"{row.Confidence.ToString("0.00", CultureInfo.InvariantCulture),-6} "
            + $"{Truncate(row.Error ?? row.RawText, 34)}");
        if (row.Error is not null && row.AttemptSummary.Length > 0)
        {
            Console.WriteLine($"{"",-8} {"",-16} └─ {Truncate(row.AttemptSummary, 88)}");
        }
    }

    Console.WriteLine();
}

static void PrintSummary(List<BenchmarkRow> rows)
{
    Console.WriteLine("汇总");
    Console.WriteLine(new string('-', 108));
    Console.WriteLine(
        $"{"配置",-18} {"正确",-8} {"正确率",-9} {"平均耗时",-11} {"平均置信度",-12} 一致性");

    foreach (var group in rows
                 .GroupBy(r => r.Configuration)
                 .OrderByDescending(g => g.Count(r => r.Correct)))
    {
        var total = group.Count();
        var correct = group.Count(r => r.Correct);
        var rate = total == 0 ? 0 : (double)correct / total;
        var avgAttempts = group.Average(r => r.AttemptCount);
        var avgSuccess = group.Average(r => r.SuccessCount);

        Console.WriteLine(
            $"{group.Key,-18} {correct + "/" + total,-8} "
            + $"{rate.ToString("P0", CultureInfo.InvariantCulture),-9} "
            + $"{group.Average(r => r.TotalMs).ToString("0.0", CultureInfo.InvariantCulture) + "ms",-11} "
            + $"{group.Average(r => r.Confidence).ToString("0.00", CultureInfo.InvariantCulture),-12} "
            + $"成功流水线 {avgSuccess:0.0}/{avgAttempts:0.0}");
    }

    Console.WriteLine();
}

static void PrintConfidenceDistribution(List<BenchmarkRow> rows)
{
    var passed = rows.Where(r => r.Correct && r.Confidence > 0).Select(r => r.Confidence).ToList();
    if (passed.Count == 0)
    {
        return;
    }

    Console.WriteLine("识别正确时的置信度分布（用于确定 MinimumConfidence 默认值）");
    Console.WriteLine(new string('-', 108));
    Console.WriteLine(
        $"最低 {passed.Min():0.00} / 25分位 {Percentile(passed, 25):0.00} / "
        + $"中位 {Percentile(passed, 50):0.00} / 75分位 {Percentile(passed, 75):0.00} / 最高 {passed.Max():0.00}");
    Console.WriteLine();
}

static double Percentile(List<double> values, double percentile)
{
    var sorted = values.OrderBy(v => v).ToList();
    var index = (int)Math.Round((percentile / 100.0) * (sorted.Count - 1));
    return sorted[Math.Clamp(index, 0, sorted.Count - 1)];
}

static void PrintRecommendation(List<BenchmarkRow> rows)
{
    Console.WriteLine("结论");
    Console.WriteLine(new string('-', 108));

    var fixtureCount = rows.Select(r => r.Fixture).Distinct().Count();

    var best = rows
        .GroupBy(r => r.Configuration)
        .Select(g => new
        {
            Name = g.Key,
            Correct = g.Count(r => r.Correct),
            Total = g.Count(),
            AvgMs = g.Average(r => r.TotalMs),
        })
        .OrderByDescending(x => x.Correct)
        .ThenBy(x => x.AvgMs)
        .FirstOrDefault();

    if (best is null)
    {
        Console.WriteLine("没有任何可用组合。");
        return;
    }

    Console.WriteLine($"最佳配置：{best.Name} （{best.Correct}/{best.Total} 正确，平均 {best.AvgMs:0.0}ms）");

    if (fixtureCount < 30)
    {
        Console.WriteLine();
        Console.WriteLine($"注意：当前只有 {fixtureCount} 个 fixture。");
        Console.WriteLine("目标基准集至少需要 30 个 ROI、覆盖多种地图区域与缩放等级，");
        Console.WriteLine("在凑齐之前不得宣称 OCR 已经稳定（目标：坐标正确率 >= 99%）。");
    }
}

/// <summary>把这次跑的结果整理成 Markdown 报告。</summary>
static string BuildMarkdownReport(FixtureManifest manifest, List<BenchmarkRow> rows)
{
    var builder = new StringBuilder();

    builder.AppendLine("# MortarHUD OCR Benchmark 报告");
    builder.AppendLine();
    builder.AppendLine("> 本文件由 `MortarHUD.Benchmark` 自动生成，请勿手工编辑。");
    builder.AppendLine("> 重新生成：`dotnet run --project tools/MortarHUD.Benchmark`");
    builder.AppendLine();

    builder.AppendLine("## 测试集");
    builder.AppendLine();
    builder.AppendLine($"- Fixture 数量：**{manifest.Fixtures.Count}**");
    builder.AppendLine($"- 参考分辨率：{manifest.ReferenceResolution.Width}x{manifest.ReferenceResolution.Height}");
    builder.AppendLine($"- ROI 默认值：offset({manifest.RoiDefaults.OffsetX},{manifest.RoiDefaults.OffsetY}) "
                       + $"{manifest.RoiDefaults.Width}x{manifest.RoiDefaults.Height}");
    builder.AppendLine();
    builder.AppendLine("| Fixture | 截图 | 期望 X | 期望 Y |");
    builder.AppendLine("|---|---|---|---|");

    foreach (var fixture in manifest.Fixtures)
    {
        builder.AppendLine($"| {fixture.Id} | {fixture.Screenshot} | "
                           + $"{fixture.ExpectedX.ToString("0.00", CultureInfo.InvariantCulture)} | "
                           + $"{fixture.ExpectedY.ToString("0.00", CultureInfo.InvariantCulture)} |");
    }

    builder.AppendLine();
    builder.AppendLine("## 结果汇总");
    builder.AppendLine();
    builder.AppendLine("| 配置 | 正确 | 正确率 | 平均耗时 | 平均置信度 | 成功流水线 |");
    builder.AppendLine("|---|---|---|---|---|---|");

    foreach (var group in rows.GroupBy(r => r.Configuration)
                 .OrderByDescending(g => g.Count(r => r.Correct))
                 .ThenBy(g => g.Average(r => r.TotalMs)))
    {
        var total = group.Count();
        var correct = group.Count(r => r.Correct);

        builder.AppendLine(
            $"| {group.Key} | {correct}/{total} | {(total == 0 ? 0 : (double)correct / total):P0} | "
            + $"{group.Average(r => r.TotalMs).ToString("0.0", CultureInfo.InvariantCulture)} ms | "
            + $"{group.Average(r => r.Confidence).ToString("0.00", CultureInfo.InvariantCulture)} | "
            + $"{group.Average(r => r.SuccessCount):0.0}/{group.Average(r => r.AttemptCount):0.0} |");
    }

    builder.AppendLine();
    builder.AppendLine("## 逐条明细");
    builder.AppendLine();
    builder.AppendLine("| Fixture | 配置 | 结果 | X | Y | 期望 X | 期望 Y | 耗时 | 说明 |");
    builder.AppendLine("|---|---|---|---|---|---|---|---|---|");

    foreach (var row in rows)
    {
        var verdict = row.Error is null ? (row.Correct ? "PASS" : "FAIL") : "ERR";

        builder.AppendLine(
            $"| {row.Fixture} | {row.Configuration} | {verdict} | {Format(row.ParsedX)} | {Format(row.ParsedY)} | "
            + $"{row.ExpectedX.ToString("0.00", CultureInfo.InvariantCulture)} | "
            + $"{row.ExpectedY.ToString("0.00", CultureInfo.InvariantCulture)} | "
            + $"{row.TotalMs.ToString("0.0", CultureInfo.InvariantCulture)} ms | "
            + $"{EscapeForMarkdownTable(Truncate(row.Error ?? TryRawText(row), 46))} |");
    }

    builder.AppendLine();
    builder.AppendLine("## 结论");
    builder.AppendLine();

    var best = rows.GroupBy(r => r.Configuration)
        .Select(g => new { Name = g.Key, Correct = g.Count(r => r.Correct), Total = g.Count() })
        .OrderByDescending(x => x.Correct)
        .FirstOrDefault();

    if (best is not null)
    {
        builder.AppendLine($"- 生产默认使用 **Auto** 配置（Tesseract + 流水线 A/C 交叉验证）。");
        builder.AppendLine($"- 本次最佳配置：**{best.Name}**（{best.Correct}/{best.Total}）。");
    }

    if (manifest.Fixtures.Count < 30)
    {
        builder.AppendLine();
        builder.AppendLine("### ⚠️ 测试集不足");
        builder.AppendLine();
        builder.AppendLine($"当前只有 **{manifest.Fixtures.Count}** 个 fixture。");
        builder.AppendLine("目标基准集至少需要 **30 个 ROI**，覆盖不同地图区域、黑/灰背景、复杂地形、");
        builder.AppendLine("目标图标附近、UI 弹窗附近与不同缩放等级。");
        builder.AppendLine("**在凑齐之前不得宣称 OCR 已经稳定**（目标：坐标正确率 >= 99%）。");
    }

    builder.AppendLine();
    return builder.ToString();
}

static string TryRawText(BenchmarkRow row)
    => string.IsNullOrWhiteSpace(row.RawText) ? row.AttemptSummary : row.RawText;

/// <summary>Markdown 表格里的竖线必须转义，否则会把表格拆散。</summary>
static string EscapeForMarkdownTable(string text) => text.Replace("|", "\\|", StringComparison.Ordinal);

/// <summary>从输出目录往上找仓库根；找不到就退回当前目录，不返回 null。</summary>
static string LocateRepoRoot()
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);

    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "MortarHUD.sln"))
            || Directory.Exists(Path.Combine(directory.FullName, "tests", "Fixtures")))
        {
            return directory.FullName;
        }

        directory = directory.Parent;
    }

    return Directory.GetCurrentDirectory();
}

static string Format(double? value) => value?.ToString("0.00", CultureInfo.InvariantCulture) ?? "-";

static string Truncate(string text, int max)
{
    var single = text.Replace("\r", "").Replace("\n", " | ").Trim();
    return single.Length <= max ? single : single[..(max - 1)] + "…";
}

internal sealed record BenchmarkConfiguration(
    string Name,
    IReadOnlyList<ICoordinateOcrEngine> Engines,
    IReadOnlyList<IImagePreprocessor> Preprocessors);

internal sealed class BenchmarkRow
{
    public string Fixture { get; set; } = "";
    public string Configuration { get; set; } = "";
    public string RawText { get; set; } = "";
    public string AttemptSummary { get; set; } = "";
    public int AttemptCount { get; set; }
    public int SuccessCount { get; set; }
    public double? ParsedX { get; set; }
    public double? ParsedY { get; set; }
    public double ExpectedX { get; set; }
    public double ExpectedY { get; set; }
    public bool Correct { get; set; }
    public double Confidence { get; set; }
    public double TotalMs { get; set; }
    public string? Error { get; set; }
}
