using System.Text.Json;
using System.Text.Json.Serialization;

namespace MortarHUD.Benchmark;

/// <summary><c>tests/Fixtures/fixtures.json</c> 的模型。</summary>
public sealed class FixtureManifest
{
    public ReferenceResolution ReferenceResolution { get; set; } = new();

    public RoiDefaults RoiDefaults { get; set; } = new();

    public List<FixtureCase> Fixtures { get; set; } = [];

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static FixtureManifest Load(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<FixtureManifest>(json, Options) ?? new FixtureManifest();
    }
}

public sealed class ReferenceResolution
{
    public int Width { get; set; }
    public int Height { get; set; }
}

public sealed class RoiDefaults
{
    public int OffsetX { get; set; }
    public int OffsetY { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}

/// <summary>一条基准用例：一张实机截图 + 当时的光标位置 + 期望坐标。</summary>
public sealed class FixtureCase
{
    public string Id { get; set; } = "";

    public string Screenshot { get; set; } = "";

    public double ExpectedX { get; set; }

    public double ExpectedY { get; set; }

    /// <summary>截图时刻的光标物理像素位置（由三图交叉验证反推）。</summary>
    public int CursorX { get; set; }

    public int CursorY { get; set; }

    public int RoiX { get; set; }
    public int RoiY { get; set; }
    public int RoiWidth { get; set; }
    public int RoiHeight { get; set; }

    /// <summary>
    /// 手工实测的坐标文字包围盒（截图绝对像素）。
    /// </summary>
    /// <remarks>
    /// 只给模板生成器用：模板生成需要在「只有文字」的干净区域里切字形，
    /// 直接在整块 ROI 上切会混进大量地图纹理（实测 46 个连通域里只有 13 个是字形）。
    /// 运行时路径不使用这个字段——那正是 OCR 要解决的问题。
    /// </remarks>
    public LabelBounds? LabelBounds { get; set; }
}

public sealed class LabelBounds
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}
