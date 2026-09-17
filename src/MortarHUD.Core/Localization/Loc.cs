namespace MortarHUD.Core.Localization;

/// <summary>
/// 界面文案的查表入口。
/// </summary>
/// <remarks>
/// <para>
/// 键是稳定的 PascalCase 标识符，中英文都在 <see cref="Strings"/> 里成对存放。
/// 用键而不是用中文原文当键，是为了让「改了中文忘了改英文」能在编译期之外
/// 被 <c>LocTableTests</c> 直接抓出来——那张表会校验中英两列都不为空、键不重复。
/// </para>
/// <para>
/// 语言由设置里的 <c>general.language</c> 驱动，启动时设定一次。
/// 取不到译文时回落中文而不是抛异常：漏翻一条不该让程序起不来。
/// </para>
/// <para>
/// <strong>日志不走这里。</strong>日志是排查用的，保持中文；
/// 全项目只有面向用户看得见的那部分文案需要本地化。
/// </para>
/// </remarks>
public static class Loc
{
    /// <summary>中文（默认）。</summary>
    public const string Chinese = "zh-CN";

    /// <summary>英文。</summary>
    public const string English = "en";

    private static string _language = Chinese;

    /// <summary>当前语言。传非 "en" 的值一律当中文处理。</summary>
    public static string Language
    {
        get => _language;
        set => _language = IsEnglishCode(value) ? English : Chinese;
    }

    /// <summary>判断一个语言代码是不是英文。大小写与区域后缀都不敏感。</summary>
    public static bool IsEnglishCode(string? language)
        => !string.IsNullOrWhiteSpace(language)
           && language.TrimStart().StartsWith("en", StringComparison.OrdinalIgnoreCase);

    /// <summary>当前是不是英文。</summary>
    public static bool IsEnglish => _language == English;

    /// <summary>按当前语言取文案。</summary>
    public static string T(string key) => Strings.Lookup(key, _language);

    /// <summary>
    /// 取带占位符的文案并填充参数，格式沿用 <see cref="string.Format(string, object[])"/>。
    /// </summary>
    /// <remarks>
    /// 原来这些地方是 <c>$"已应用（{DateTime.Now:HH:mm:ss}）。"</c> 这样的内插字符串，
    /// 没法直接换成本地化调用——模板必须整条查表，参数另外传进来。
    /// </remarks>
    public static string F(string key, params object?[] args) => string.Format(T(key), args);
}
