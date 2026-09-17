using MortarHUD.Platform.Windows.NativeMethods;

namespace MortarHUD.Platform.Windows.Hotkeys;

/// <summary>
/// 在 <c>"Ctrl+Shift+G"</c> / <c>"MouseMiddle"</c> 与 <see cref="HotkeyDefinition"/> 之间互转
/// （TDD §18 / §29）。
/// </summary>
public static class HotkeyParser
{
    /// <summary>解析配置文件里的字符串。失败时返回 <see cref="HotkeyDefinition.None"/>。</summary>
    public static bool TryParse(string? text, out HotkeyDefinition definition, out string? error)
    {
        definition = HotkeyDefinition.None;
        error = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            error = "热键为空。";
            return false;
        }

        uint modifiers = 0;
        uint virtualKey = 0;
        var device = HotkeyDevice.Keyboard;
        var keySeen = false;

        foreach (var rawToken in text.Split('+', StringSplitOptions.TrimEntries))
        {
            if (rawToken.Length == 0)
            {
                continue;
            }

            switch (rawToken.ToLowerInvariant())
            {
                case "ctrl" or "control":
                    modifiers |= Win32.MOD_CONTROL;
                    continue;
                case "alt":
                    modifiers |= Win32.MOD_ALT;
                    continue;
                case "shift":
                    modifiers |= Win32.MOD_SHIFT;
                    continue;
                case "win" or "windows" or "meta" or "super":
                    modifiers |= Win32.MOD_WIN;
                    continue;
            }

            if (keySeen)
            {
                error = $"「{text}」里有多个主键，一个热键只能有一个主键。";
                return false;
            }

            // 先试鼠标键：RegisterHotKey 不支持鼠标，必须走钩子，所以这里要区分开。
            if (MouseButtonNames.TryParse(rawToken, out var mouseButton))
            {
                virtualKey = (uint)mouseButton;
                device = HotkeyDevice.Mouse;
                keySeen = true;
                continue;
            }

            if (!VirtualKeys.TryGetCode(rawToken, out virtualKey))
            {
                error = $"无法识别的按键「{rawToken}」。";
                return false;
            }

            device = HotkeyDevice.Keyboard;
            keySeen = true;
        }

        if (!keySeen)
        {
            error = $"「{text}」里没有主键。";
            return false;
        }

        definition = new HotkeyDefinition(modifiers, virtualKey, device);
        return true;
    }

    public static HotkeyDefinition ParseOrDefault(string? text, HotkeyDefinition fallback)
        => TryParse(text, out var definition, out _) ? definition : fallback;
}
