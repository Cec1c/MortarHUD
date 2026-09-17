namespace MortarHUD.Platform.Windows.Hotkeys;

/// <summary>
/// 热键可用的按键名 ↔ 虚拟键码。
/// </summary>
/// <remarks>
/// 刻意不引用 System.Windows.Forms 或 WPF 的按键枚举，
/// 这样本程序集不需要任何 UI 框架依赖，也能被纯单元测试引用。
/// </remarks>
public static class VirtualKeys
{
    private static readonly Dictionary<string, uint> NameToCode =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<uint, string> CodeToName = [];

    static VirtualKeys()
    {
        for (var i = 1; i <= 24; i++)
        {
            Add($"F{i}", (uint)(0x70 + i - 1));
        }

        for (var c = 'A'; c <= 'Z'; c++)
        {
            Add(c.ToString(), c);
        }

        for (var d = '0'; d <= '9'; d++)
        {
            Add(d.ToString(), d);
        }

        // 小键盘：名字里带 Numpad 前缀，避免和主键盘数字冲突。
        for (var i = 0; i <= 9; i++)
        {
            Add($"Numpad{i}", (uint)(0x60 + i));
        }

        Add("NumpadMultiply", 0x6A);
        Add("NumpadAdd", 0x6B);
        Add("NumpadSubtract", 0x6D);
        Add("NumpadDecimal", 0x6E);
        Add("NumpadDivide", 0x6F);

        Add("Backspace", 0x08);
        Add("Tab", 0x09);
        Add("Enter", 0x0D);
        Add("Return", 0x0D);
        Add("Pause", 0x13);
        Add("CapsLock", 0x14);
        Add("Escape", 0x1B);
        Add("Esc", 0x1B);
        Add("Space", 0x20);
        Add("PageUp", 0x21);
        Add("PageDown", 0x22);
        Add("End", 0x23);
        Add("Home", 0x24);
        Add("Left", 0x25);
        Add("Up", 0x26);
        Add("Right", 0x27);
        Add("Down", 0x28);
        Add("PrintScreen", 0x2C);
        Add("Insert", 0x2D);
        Add("Delete", 0x2E);
        Add("NumLock", 0x90);
        Add("ScrollLock", 0x91);

        Add("OemSemicolon", 0xBA);
        Add("OemPlus", 0xBB);
        Add("OemComma", 0xBC);
        Add("OemMinus", 0xBD);
        Add("OemPeriod", 0xBE);
        Add("OemQuestion", 0xBF);
        Add("OemTilde", 0xC0);
        Add("OemOpenBrackets", 0xDB);
        Add("OemPipe", 0xDC);
        Add("OemCloseBrackets", 0xDD);
        Add("OemQuotes", 0xDE);
    }

    private static void Add(string name, uint code)
    {
        // 别名（如 Enter/Return）指向同一个码，反向表保留第一个名字作为规范名。
        NameToCode[name] = code;
        CodeToName.TryAdd(code, name);
    }

    public static bool TryGetCode(string name, out uint code)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            code = 0;
            return false;
        }

        return NameToCode.TryGetValue(name.Trim(), out code);
    }

    public static string GetName(uint code)
        => CodeToName.TryGetValue(code, out var name) ? name : $"0x{code:X2}";

    public static bool IsKnown(uint code) => CodeToName.ContainsKey(code);
}
