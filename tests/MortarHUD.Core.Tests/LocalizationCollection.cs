using Xunit;

namespace MortarHUD.Core.Tests;

/// <summary>
/// 碰界面文案的测试跑在同一个集合里，且不与其它集合并行。
/// </summary>
/// <remarks>
/// <para>
/// <see cref="MortarHUD.Localization.Loc.Language"/> 是<strong>静态</strong>可变的。
/// <c>LocTableTests</c> 为了验英文表会临时把它切成英文再切回来，
/// 而 <c>HudLayoutFormatterTests</c> 断言的正是中文文案。
/// </para>
/// <para>
/// 两者不带集合标注时会并行跑，于是前者的「临时切英文」会漏进后者，
/// 表现为「目标未改变」断言成 "Target unchanged"——本地跑不出来、
/// CI 上偶发（实测在 GitHub Actions 上红过一次）。
/// 静态可变状态 + 并行 = 顺序说了算的假失败，只能靠串行化解决。
/// </para>
/// <para>
/// <see cref="CollectionDefinitionAttribute.DisableParallelization"/> 让这个集合
/// 与<em>其它所有</em>集合也串行——不止是这两类之间。因为任何测试都可能间接
/// 依赖当前语言（比如某个断言里带了本地化文案而改用例的人没意识到）。
/// </para>
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class LocalizationCollection
{
    public const string Name = "Localization";
}
