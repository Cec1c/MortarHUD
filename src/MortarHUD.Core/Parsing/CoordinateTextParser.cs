using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MortarHUD.Core.Parsing;

/// <summary>
/// 解析游戏在鼠标旁绘制的坐标读数。
/// </summary>
/// <remarks>
/// <para>
/// 文本形如：
/// <code>
/// y109.78
/// x98.09
/// </code>
/// x/y 的先后顺序不固定，两轴各自独立匹配。
/// </para>
/// <para>
/// 明确<strong>不做</strong>的事：不会把 x10766 猜成 x107.66。
/// 小数点必须由 OCR 自己读出来，读不出来就判定失败（TDD §15）。
/// </para>
/// </remarks>
public sealed partial class CoordinateTextParser : ICoordinateTextParser
{
    private readonly CoordinateTextParserOptions _options;

    public CoordinateTextParser(CoordinateTextParserOptions? options = null)
        => _options = options ?? CoordinateTextParserOptions.Default;

    public CoordinateParseResult Parse(string? rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return CoordinateParseResult.Fail("EMPTY_OCR_TEXT");
        }

        var repaired = Repair(rawText);

        var xCandidates = MatchAxis(repaired, 'x');
        var yCandidates = MatchAxis(repaired, 'y');

        if (xCandidates.Count == 0)
        {
            return CoordinateParseResult.Fail("X_NOT_FOUND", repaired);
        }

        if (yCandidates.Count == 0)
        {
            return CoordinateParseResult.Fail("Y_NOT_FOUND", repaired);
        }

        if (_options.RejectAmbiguousAxis)
        {
            if (DistinctCount(xCandidates) > 1)
            {
                return CoordinateParseResult.Fail("AMBIGUOUS_X", repaired);
            }

            if (DistinctCount(yCandidates) > 1)
            {
                return CoordinateParseResult.Fail("AMBIGUOUS_Y", repaired);
            }
        }

        return CoordinateParseResult.Ok(xCandidates[0], yCandidates[0], repaired);
    }

    private static int DistinctCount(List<double> values)
    {
        for (var i = 1; i < values.Count; i++)
        {
            if (!values[i].Equals(values[0]))
            {
                return 2;
            }
        }

        return values.Count == 0 ? 0 : 1;
    }

    private List<double> MatchAxis(string text, char axis)
    {
        var results = new List<double>();
        var pattern = axis == 'x' ? XRegex() : YRegex();

        foreach (Match match in pattern.Matches(text))
        {
            var literal = match.Groups["num"].Value;

            if (_options.RequireDecimalPoint && !literal.Contains('.'))
            {
                // 小数点是硬性要求。宁可失败，也不要拿一个可能被截断的整数当坐标。
                continue;
            }

            if (!TryValidateShape(literal))
            {
                continue;
            }

            if (double.TryParse(literal, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                && double.IsFinite(value))
            {
                results.Add(value);
            }
        }

        return results;
    }

    /// <summary>
    /// 对正则已经匹配到的数字字面量再做一次形状校验：
    /// 整数位不超过上限、小数位必须在允许集合内。
    /// </summary>
    private bool TryValidateShape(string literal)
    {
        var body = literal.StartsWith('-') ? literal[1..] : literal;
        var dot = body.IndexOf('.');

        if (dot < 0)
        {
            // 正则本身已限定 1..3 位，这里再挡一次 0 位的情况。
            return body.Length is >= 1 && body.Length <= _options.MaxIntegerDigits;
        }

        var integerPart = body[..dot];
        var fractionPart = body[(dot + 1)..];

        if (integerPart.Length is < 1 || integerPart.Length > _options.MaxIntegerDigits)
        {
            return false;
        }

        if (!_options.AllowedFractionDigits.Contains(fractionPart.Length))
        {
            return false;
        }

        // 去掉前导零后仍然只能是纯数字——正则已保证，此处仅防御性检查。
        return fractionPart.All(char.IsAsciiDigit);
    }

    /// <summary>
    /// 规范化 + 有限字符修正。
    /// 修正只作用于紧跟 x/y 之后的数字串，绝不改动其他位置的字符。
    /// </summary>
    private string Repair(string rawText)
    {
        var normalized = NormalizeLookalikes(rawText);
        var builder = new StringBuilder(normalized.Length + 8);

        var i = 0;
        while (i < normalized.Length)
        {
            var c = normalized[i];

            if (!IsAxisLetter(c) || !IsAxisBoundary(normalized, i))
            {
                builder.Append(c);
                i++;
                continue;
            }

            builder.Append(c);
            i++;

            // 轴字母与数字之间允许出现分隔符：空格、冒号（半角/全角）、等号。
            while (i < normalized.Length && IsSeparator(normalized[i]))
            {
                builder.Append(normalized[i]);
                i++;
            }

            if (!_options.EnableDigitRepair)
            {
                continue;
            }

            // 只修正紧随其后的这一串数字/易混字符。
            while (i < normalized.Length)
            {
                var digitLike = normalized[i];
                if (DigitRepairMap.TryGetValue(digitLike, out var replacement))
                {
                    builder.Append(replacement);
                    i++;
                }
                else if (char.IsAsciiDigit(digitLike) || digitLike == '.')
                {
                    builder.Append(digitLike);
                    i++;
                }
                else
                {
                    break;
                }
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// 把 Unicode 里长得像 x/y/数字的字符换成 ASCII。
    /// OCR 引擎在游戏字体上经常吐出这些「看着一样」的码位。
    /// </summary>
    private static string NormalizeLookalikes(string text)
    {
        var builder = new StringBuilder(text.Length);

        foreach (var c in text)
        {
            builder.Append(c switch
            {
                '×' or 'х' or 'ⅹ' or 'Ｘ' or 'ｘ' => 'x',
                '¥' or 'у' or 'Ｙ' or 'ｙ' or 'Ｖ' => 'y',
                '：' or '∶' => ':',
                '．' or '·' or '。' => '.',
                '０' => '0', '１' => '1', '２' => '2', '３' => '3', '４' => '4',
                '５' => '5', '６' => '6', '７' => '7', '８' => '8', '９' => '9',
                '－' => '-',
                _ => c,
            });
        }

        return builder.ToString();
    }

    private static bool IsAxisLetter(char c) => c is 'x' or 'X' or 'y' or 'Y';

    /// <summary>
    /// 轴字母与数字之间允许出现的字符。
    /// </summary>
    /// <remarks>
    /// 除空白与冒号外，还放进了一批 OCR 常见的噪声标点——实测 Tesseract 会把
    /// 「y109.78」读成「y.109.78」。这类修正不涉及数字本身的猜测，是安全的：
    /// 因为 <see cref="CoordinateTextParserOptions.RequireDecimalPoint"/> 仍然要求
    /// 数字里必须自带小数点，所以「x.98」不会被误当成 98，只会被判为失败。
    /// <para>
    /// 注意不能把 '|' 放进来——它按 TDD §15 是数字 1 的修正来源，
    /// 当成分隔符会把 y|14.54 错解成 14.54。
    /// </para>
    /// </remarks>
    private static bool IsSeparator(char c)
        => c is ' ' or '\t' or ':' or '=' or '.' or ',' or ';' or '\'' or '·' or '"';

    /// <summary>
    /// 轴字母左边不能紧跟另一个字母（否则是单词的一部分，例如 "Max107"）。
    /// 但允许紧跟数字，因为 OCR 常把两行粘成 "110.07x99.58"。
    /// </summary>
    private static bool IsAxisBoundary(string text, int index)
    {
        if (index == 0)
        {
            return true;
        }

        var previous = text[index - 1];
        return !char.IsAsciiLetter(previous);
    }

    /// <summary>
    /// 仅 TDD §15 授权的替换：O/o → 0，I/l → 1。
    /// 额外加上 '|' → 1，因为竖线在坐标语境里不可能是合法字符。
    /// 其它相似字符（S/B/Z/g 等）一律不猜。
    /// </summary>
    private static readonly Dictionary<char, char> DigitRepairMap = new()
    {
        ['O'] = '0',
        ['o'] = '0',
        ['I'] = '1',
        ['l'] = '1',
        ['|'] = '1',
    };

    // 整数 1..3 位，小数位由 AllowedFractionDigits 在 TryValidateShape 里把关，
    // 所以正则先放宽到 1..3 位，再用 (?![0-9]) 保证数字串确实到此为止——
    // 正是这个 lookahead 让 "x10766" 整体匹配失败，而不是退化成 "x107"。
    // 分隔符类与 IsSeparator 保持一致，多出来的标点用于吃掉 OCR 塞在轴字母
    // 和数字之间的噪声（实测有 "y.109.78"、"-7x98.09"）。
    [GeneratedRegex(@"(?<![A-Za-z])[xX][\s:：=.,;'""·]*?(?<num>-?\d{1,3}(?:\.\d{1,3})?)(?![0-9])",
        RegexOptions.CultureInvariant)]
    private static partial Regex XRegex();

    [GeneratedRegex(@"(?<![A-Za-z])[yY][\s:：=.,;'""·]*?(?<num>-?\d{1,3}(?:\.\d{1,3})?)(?![0-9])",
        RegexOptions.CultureInvariant)]
    private static partial Regex YRegex();
}
