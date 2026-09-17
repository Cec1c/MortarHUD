using System.Text.Json;
using MortarHUD.Localization;
using System.Text.Json.Serialization;
using OpenCvSharp;

namespace MortarHUD.Capture.Ocr;

/// <summary>模板库里的一条：某个字符在某条预处理流水线下的标准样子。</summary>
public sealed record GlyphTemplate(string Label, string Pipeline);

/// <summary>
/// 字形模板库：一组「归一化后的字形位图 + 对应的字符」。
/// </summary>
/// <remarks>
/// <para>
/// 游戏 UI 文字是固定字体、固定字号的点阵渲染，同一个字符每次画出来几乎是同一张图，
/// 所以模板匹配在这个场景下特别合适：不需要语言模型，也不需要外部依赖。
/// </para>
/// <para>
/// <strong>为什么每个字符要存多个变体：</strong>三条预处理流水线产出的二值化图
/// 笔画粗细差别很大（顶帽那条会把笔画削细，全局阈值那条保留粗笔画）。
/// 同一张字形在不同流水线下归一化之后差别足以拉低匹配分数，
/// 因此按流水线各存一份，匹配时取最高分——引擎不需要知道输入来自哪条流水线。
/// </para>
/// <para>
/// 模板来自实机截图，由 <c>MortarHUD.Benchmark --gen-templates</c> 生成。
/// 覆盖范围见 <see cref="Coverage"/>，缺哪些字符是明摆着的，不会静默出错。
/// </para>
/// </remarks>
public sealed class GlyphAtlas
{
    /// <summary>归一化后的模板尺寸。所有字形都会被缩放进这个方框再比较。</summary>
    public const int TemplateWidth = 20;
    public const int TemplateHeight = 28;

    public const string SpriteFileName = "glyphs.png";
    public const string LabelsFileName = "glyphs.json";

    /// <summary>旧版本用纯字符串数组，这里兼容读一下，省得旧文件直接把引擎搞挂。</summary>
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private GlyphAtlas(IReadOnlyList<GlyphTemplate> entries, IReadOnlyList<Mat> templates)
    {
        Entries = entries;
        Templates = templates;
    }

    public IReadOnlyList<GlyphTemplate> Entries { get; }

    private IReadOnlyList<Mat> Templates { get; }

    /// <summary>已覆盖的字符集合（去重），Debug 面板用它说明「哪些字符认得」。</summary>
    public string Coverage => string.Concat(
        Entries.Select(e => e.Label).Distinct(StringComparer.Ordinal).OrderBy(l => l, StringComparer.Ordinal));

    public int Count => Entries.Count;

    /// <summary>在全部变体里找最像的那个，返回字符与相似度。</summary>
    public (string Label, double Score) Match(Mat normalizedGlyph)
    {
        var bestLabel = "?";
        var bestScore = 0.0;

        for (var i = 0; i < Entries.Count; i++)
        {
            var score = GlyphNormalizer.Similarity(normalizedGlyph, Templates[i]);
            if (score > bestScore)
            {
                bestScore = score;
                bestLabel = Entries[i].Label;
            }
        }

        return (bestLabel, bestScore);
    }

    /// <summary>从目录加载雪碧图与标签表。任一缺失或损坏都返回 null。</summary>
    public static GlyphAtlas? TryLoad(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return null;
        }

        var spritePath = Path.Combine(directory, SpriteFileName);
        var labelsPath = Path.Combine(directory, LabelsFileName);

        if (!File.Exists(spritePath) || !File.Exists(labelsPath))
        {
            return null;
        }

        try
        {
            var entries = JsonSerializer.Deserialize<List<GlyphTemplate>>(
                File.ReadAllText(labelsPath), SerializerOptions);

            if (entries is null || entries.Count == 0)
            {
                return null;
            }

            using var sprite = Cv2.ImRead(spritePath, ImreadModes.Grayscale);
            if (sprite.Empty() || sprite.Height < entries.Count * TemplateHeight)
            {
                return null;
            }

            var templates = new List<Mat>(entries.Count);
            for (var i = 0; i < entries.Count; i++)
            {
                var rect = new Rect(0, i * TemplateHeight, TemplateWidth, TemplateHeight);
                templates.Add(new Mat(sprite, rect).Clone());
            }

            return new GlyphAtlas(entries, templates);
        }
        catch (Exception ex) when (ex is IOException or JsonException or OpenCVException)
        {
            System.Diagnostics.Debug.WriteLine($"字形模板库加载失败：{ex.Message}");
            return null;
        }
    }

    /// <summary>把模板拼成雪碧图写盘。</summary>
    public static void Save(string directory, IReadOnlyList<GlyphTemplate> entries, IReadOnlyList<Mat> templates)
    {
        if (entries.Count != templates.Count)
        {
            throw new ArgumentException(Loc.T("TheTemplateCountDoesNotMatchTheBitmapCount"), nameof(templates));
        }

        Directory.CreateDirectory(directory);

        using var sprite = new Mat(
            new Size(TemplateWidth, TemplateHeight * templates.Count),
            MatType.CV_8UC1,
            Scalar.All(0));

        for (var i = 0; i < templates.Count; i++)
        {
            var target = new Rect(0, i * TemplateHeight, TemplateWidth, TemplateHeight);
            using var view = new Mat(sprite, target);
            templates[i].CopyTo(view);
        }

        Cv2.ImWrite(Path.Combine(directory, SpriteFileName), sprite);
        File.WriteAllText(
            Path.Combine(directory, LabelsFileName),
            JsonSerializer.Serialize(entries, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            }));
    }
}
