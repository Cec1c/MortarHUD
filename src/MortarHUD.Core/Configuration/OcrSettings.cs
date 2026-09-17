namespace MortarHUD.Core.Configuration;

/// <summary>OCR 引擎标识。</summary>
public static class OcrEngineNames
{
    /// <summary>先跑 Tesseract，失败则退到模板匹配。</summary>
    public const string Auto = "Auto";

    public const string Tesseract = "Tesseract";

    /// <summary>自定义字形模板匹配，零外部依赖。</summary>
    public const string Template = "Template";
}

/// <summary>预处理流水线标识（TDD §14）。</summary>
public static class PreprocessorNames
{
    /// <summary>依次尝试 A → B → C，取第一个通过校验的结果。</summary>
    public const string Auto = "Auto";

    public const string A = "A";
    public const string B = "B";
    public const string C = "C";
}

public sealed class OcrSettings
{
    public string Engine { get; set; } = OcrEngineNames.Auto;

    public string Preprocessor { get; set; } = PreprocessorNames.Auto;

    /// <summary>坐标下限，同时喂给解析与校验。</summary>
    public double CoordinateMin { get; set; } = 0.0;

    public double CoordinateMax { get; set; } = 200.0;

    public double MinimumConfidence { get; set; } = 0.60;

    /// <summary>tessdata 目录；留空表示用程序目录下的 Models/tessdata。</summary>
    public string TessdataPath { get; set; } = "";

    /// <summary>Tesseract 语言包。数字识别只需要 eng。</summary>
    public string Language { get; set; } = "eng";

    /// <summary>字符白名单。收窄搜索空间能显著提升数字识别率。</summary>
    public string CharacterWhitelist { get; set; } = "0123456789xy.:-";

    /// <summary>
    /// Tesseract PageSegMode，默认 11 = 稀疏文本。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 为什么不是 6（单一文本块）：坐标是「y 行在上、x 行在下」的两行稀疏文字，
    /// 6 会把两行当成<em>一个</em>文本块统一切分。实测在 117 份实机采集上回放，
    /// 6 会把末位数字读错（同一张图 A 流水线读 99.75、C 读 99.73），
    /// 触发 <c>PIPELINE_DISAGREEMENT</c> 直接判失败；11 则两票都读对。
    /// </para>
    /// <para>
    /// 回放对比（成功 / 两票矛盾）：6 是 65/117 与 16 次，11 是 79/117 与 5 次。
    /// 单独把 x 行裁出来喂给引擎时两条流水线都能读对，说明误读确实出在版面切分上。
    /// </para>
    /// </remarks>
    public int PageSegMode { get; set; } = 11;

    /// <summary>是否要求小数点必须被识别出来（见 TDD §15）。</summary>
    public bool RequireDecimalPoint { get; set; } = true;

    public OcrSettings Clone() => new()
    {
        Engine = Engine,
        Preprocessor = Preprocessor,
        CoordinateMin = CoordinateMin,
        CoordinateMax = CoordinateMax,
        MinimumConfidence = MinimumConfidence,
        TessdataPath = TessdataPath,
        Language = Language,
        CharacterWhitelist = CharacterWhitelist,
        PageSegMode = PageSegMode,
        RequireDecimalPoint = RequireDecimalPoint,
    };
}
