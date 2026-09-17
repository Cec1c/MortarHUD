namespace MortarHUD.Platform.Windows.Hotkeys;

/// <summary>
/// 注册全局热键。程序只监听输入，绝不模拟输入（TDD §0 / §18）。
/// </summary>
public interface IGlobalHotkeyService : IDisposable
{
    event EventHandler<HotkeyPressedEventArgs>? HotkeyPressed;

    /// <summary>
    /// 用一批绑定替换当前全部绑定。
    /// 冲突的绑定会被跳过，逐条返回失败原因而不是整体抛异常——
    /// 一个键被别的软件占了，不该导致其它三个功能也不能用。
    /// </summary>
    IReadOnlyList<HotkeyRegistrationResult> Apply(IReadOnlyDictionary<HotkeyAction, HotkeyDefinition> bindings);

    /// <summary>
    /// 注册<b>观察型</b>热键：只监听，不拦截。
    /// </summary>
    /// <remarks>
    /// 用于「按 M 打开地图时自动校准炮位」这类场景：
    /// 被观察的键必须照常送到游戏手里，否则地图打不开，功能也就无从谈起。
    /// 只有低级键盘钩子能做到这一点——RegisterHotKey 是抢占式的。
    /// </remarks>
    IReadOnlyList<HotkeyRegistrationResult> ApplyObserved(
        IReadOnlyDictionary<HotkeyAction, HotkeyDefinition> bindings);

    void UnregisterAll();
}
