using System.Windows.Markup;
using MortarHUD.Core.Localization;

namespace MortarHUD.App.Localization;

/// <summary>
/// XAML 里取本地化文案：<c>Text="{loc:T DailyUse}"</c>。
/// </summary>
/// <remarks>
/// <para>
/// 键是 PascalCase 标识符，不含空格和逗号——MarkupExtension 的位置参数在这两处会断开，
/// 而界面文案里带空格的很常见（「显示 / 隐藏 HUD」），
/// 所以不能拿中文原文当键直接写进 XAML。
/// </para>
/// <para>
/// 文案在窗口构造时求值一次。语言切换要重启程序才生效，设置页里写明了这一点。
/// </para>
/// </remarks>
public sealed class TExtension : MarkupExtension
{
    public TExtension()
    {
    }

    public TExtension(string key) => Key = key;

    /// <summary>文案表里的键。</summary>
    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider) => Loc.T(Key);
}
