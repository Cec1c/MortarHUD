using MortarHUD.Platform.Windows.NativeMethods;

namespace MortarHUD.Platform.Windows.Hotkeys;

/// <summary>一个具体的热键：修饰键 + 主键（键盘按键或鼠标键）。</summary>
public readonly record struct HotkeyDefinition(
    uint Modifiers,
    uint VirtualKey,
    HotkeyDevice Device = HotkeyDevice.Keyboard)
{
    /// <summary>没有主键即为无效绑定。</summary>
    public bool IsValid => VirtualKey != 0;

    /// <summary>是不是鼠标热键。鼠标键走低级钩子，键盘键走 RegisterHotKey。</summary>
    public bool IsMouse => Device == HotkeyDevice.Mouse;

    public bool HasControl => (Modifiers & Win32.MOD_CONTROL) != 0;

    public bool HasAlt => (Modifiers & Win32.MOD_ALT) != 0;

    public bool HasShift => (Modifiers & Win32.MOD_SHIFT) != 0;

    public bool HasWin => (Modifiers & Win32.MOD_WIN) != 0;

    /// <summary>送进 RegisterHotKey 的修饰位（含 MOD_NOREPEAT）。</summary>
    public uint RegistrationModifiers => Modifiers | Win32.MOD_NOREPEAT;

    public static HotkeyDefinition None => default;

    /// <summary>规范化成 "Ctrl+Shift+G" / "MouseMiddle" 这样的稳定写法，可直接存进 settings.json。</summary>
    public string ToConfigString()
    {
        if (!IsValid)
        {
            return "";
        }

        var parts = new List<string>(5);

        if (HasControl)
        {
            parts.Add("Ctrl");
        }

        if (HasAlt)
        {
            parts.Add("Alt");
        }

        if (HasShift)
        {
            parts.Add("Shift");
        }

        if (HasWin)
        {
            parts.Add("Win");
        }

        parts.Add(IsMouse
            ? MouseButtonNames.GetName((MouseButton)VirtualKey)
            : VirtualKeys.GetName(VirtualKey));

        return string.Join("+", parts);
    }

    /// <summary>界面上显示的名字，鼠标键用中文。</summary>
    public string ToDisplayString()
    {
        if (!IsValid)
        {
            return "（未设置）";
        }

        if (IsMouse)
        {
            var prefix = BuildModifierPrefix();
            return prefix + MouseButtonNames.GetDisplayName((MouseButton)VirtualKey);
        }

        return ToConfigString();
    }

    private string BuildModifierPrefix()
    {
        var parts = new List<string>(4);

        if (HasControl)
        {
            parts.Add("Ctrl+");
        }

        if (HasAlt)
        {
            parts.Add("Alt+");
        }

        if (HasShift)
        {
            parts.Add("Shift+");
        }

        if (HasWin)
        {
            parts.Add("Win+");
        }

        return string.Concat(parts);
    }

    public override string ToString() => ToConfigString();
}
