using System.Text.Json;
using System.Text.Json.Serialization;
using MortarHUD.Capture.ScreenCapture;
using OpenCvSharp;

namespace MortarHUD.Ocr.Tests;

/// <summary><c>tests/Fixtures/fixtures.json</c> 里的一条用例。</summary>
public sealed class FixtureCase
{
    public string Id { get; set; } = "";
    public string Screenshot { get; set; } = "";
    public double ExpectedX { get; set; }
    public double ExpectedY { get; set; }
    public int CursorX { get; set; }
    public int CursorY { get; set; }
    public int RoiX { get; set; }
    public int RoiY { get; set; }
    public int RoiWidth { get; set; }
    public int RoiHeight { get; set; }
}

public sealed class FixtureManifest
{
    public List<FixtureCase> Fixtures { get; set; } = [];
}

/// <summary>
/// 读取实机截图测试集。
/// </summary>
/// <remarks>
/// OCR 的测试必须跑在**真实截图**上。用合成图测 OCR 等于自己出题自己答：
/// 合成图的噪声分布和游戏画面完全不同，真实环境里该错的照样错。
/// </remarks>
public static class FixtureRepository
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private static string? _repoRoot;

    public static string RepoRoot => _repoRoot ??= LocateRepoRoot();

    public static string ScreenshotDirectory => Path.Combine(RepoRoot, "tests", "Fixtures", "screenshots");

    public static string TessdataDirectory =>
        Path.Combine(RepoRoot, "src", "MortarHUD.App", "Models", "tessdata");

    public static string GlyphAtlasDirectory =>
        Path.Combine(RepoRoot, "src", "MortarHUD.App", "Models", "glyphs");

    public static IReadOnlyList<FixtureCase> Load()
    {
        var path = Path.Combine(RepoRoot, "tests", "Fixtures", "fixtures.json");
        var json = File.ReadAllText(path);

        return JsonSerializer.Deserialize<FixtureManifest>(json, Options)?.Fixtures ?? [];
    }

    /// <summary>按 manifest 里记录的 ROI 裁出这张 fixture 的截图区域。</summary>
    public static Mat LoadRoi(FixtureCase fixture)
    {
        var screenshotPath = Path.Combine(ScreenshotDirectory, fixture.Screenshot);
        var full = Cv2.ImRead(screenshotPath, ImreadModes.Color);

        if (full.Empty())
        {
            throw new InvalidOperationException($"截图读取失败：{screenshotPath}");
        }

        var rect = RoiCalculator.ClampTo(
            new System.Drawing.Rectangle(fixture.RoiX, fixture.RoiY, fixture.RoiWidth, fixture.RoiHeight),
            new System.Drawing.Rectangle(0, 0, full.Width, full.Height));

        var roi = new Mat(full, new Rect(rect.X, rect.Y, rect.Width, rect.Height)).Clone();
        full.Dispose();

        return roi;
    }

    private static string LocateRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "tests", "Fixtures")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"找不到 tests/Fixtures 目录，起点是 {AppContext.BaseDirectory}");
    }
}
