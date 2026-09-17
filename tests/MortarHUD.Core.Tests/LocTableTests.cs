using System.Text.RegularExpressions;
using MortarHUD.Localization;
using Xunit;

namespace MortarHUD.Core.Tests;

/// <summary>
/// 文案表的完整性（TDD §20 的界面文案）。
/// </summary>
/// <remarks>
/// 这些断言看着琐碎，但它们拦的是同一类事故：新加一条文案只填了中文，
/// 英文用户看到一个空串或一个键名。那种错误在中文环境下永远测不出来。
/// </remarks>
public sealed class LocTableTests
{
    private static readonly Regex Han = new(@"[一-鿿]", RegexOptions.Compiled);

    [Fact]
    public void TableIsNotEmpty()
    {
        Assert.NotEmpty(Strings.All);
    }

    [Fact]
    public void EveryEntryHasBothLanguages()
    {
        var missing = Strings.All
            .Where(e => string.IsNullOrWhiteSpace(e.Value.Zh) || string.IsNullOrWhiteSpace(e.Value.En))
            .Select(e => e.Key)
            .ToList();

        Assert.True(missing.Count == 0, "这些键缺一门外语：" + string.Join(", ", missing));
    }

    /// <summary>英文那一列不该再出现汉字——那多半是复制粘贴时忘了翻。</summary>
    [Fact]
    public void EnglishColumnContainsNoChinese()
    {
        var untranslated = Strings.All
            .Where(e => Han.IsMatch(e.Value.En))
            .Select(e => $"{e.Key} → {e.Value.En}")
            .ToList();

        Assert.True(untranslated.Count == 0, "这些条目英文列还是中文：" + string.Join(" | ", untranslated));
    }

    /// <summary>键在表里查不到时返回键名本身，界面上会露出一个显眼的占位串而不是空白。</summary>
    [Fact]
    public void UnknownKeyFallsBackToTheKeyItself()
    {
        Assert.Equal("NoSuchKeyAtAll", Loc.T("NoSuchKeyAtAll"));
    }

    [Fact]
    public void ChineseIsTheDefaultLanguage()
    {
        Assert.Equal(Loc.Chinese, Loc.Language);
        Assert.False(Loc.IsEnglish);
    }

    [Fact]
    public void SwitchingLanguageChangesLookup()
    {
        var original = Loc.Language;
        try
        {
            Loc.Language = Loc.English;
            Assert.Equal("Gun locked", Loc.T("GunLocked"));

            Loc.Language = Loc.Chinese;
            Assert.Equal("炮位已锁定", Loc.T("GunLocked"));
        }
        finally
        {
            Loc.Language = original;
        }
    }

    /// <summary>设置里存的可能是 "en-US" 这类带区域的写法，也得认。</summary>
    [Theory]
    [InlineData("en", true)]
    [InlineData("en-US", true)]
    [InlineData("EN", true)]
    [InlineData("zh-CN", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void LanguageCodeIsMatchedLoosely(string? code, bool expected)
    {
        Assert.Equal(expected, Loc.IsEnglishCode(code));
    }

    /// <summary>占位符模板走 string.Format，参数按顺序填进去。</summary>
    [Fact]
    public void FormatFillsPlaceholders()
    {
        var original = Loc.Language;
        try
        {
            Loc.Language = Loc.English;
            Assert.Equal("Applied at 12:34:56.", Loc.F("AppliedAt", new DateTime(2026, 1, 1, 12, 34, 56)));

            Loc.Language = Loc.Chinese;
            Assert.Equal("已应用（12:34:56）。", Loc.F("AppliedAt", new DateTime(2026, 1, 1, 12, 34, 56)));
        }
        finally
        {
            Loc.Language = original;
        }
    }
}
