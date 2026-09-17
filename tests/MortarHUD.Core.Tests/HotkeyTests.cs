using MortarHUD.Platform.Windows.Hotkeys;
using Xunit;

namespace MortarHUD.Core.Tests;

/// <summary>热键解析与格式化测试（TDD §18 / §29）。</summary>
public class HotkeyTests
{
    [Theory]
    [InlineData("F6", 0u, 0x75u)]
    [InlineData("f7", 0u, 0x76u)]
    [InlineData("Ctrl+G", 0x0002u, 0x47u)]
    [InlineData("Ctrl+Shift+G", 0x0002u | 0x0004u, 0x47u)]
    [InlineData("Alt+F8", 0x0001u, 0x77u)]
    [InlineData("Ctrl+Alt+Shift+Win+K", 0x0002u | 0x0001u | 0x0004u | 0x0008u, 0x4Bu)]
    [InlineData("Numpad5", 0u, 0x65u)]
    [InlineData("Space", 0u, 0x20u)]
    public void TryParse_ValidStrings_Succeed(string text, uint expectedModifiers, uint expectedKey)
    {
        var ok = HotkeyParser.TryParse(text, out var definition, out var error);

        Assert.True(ok, error);
        Assert.Equal(expectedModifiers, definition.Modifiers);
        Assert.Equal(expectedKey, definition.VirtualKey);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Ctrl")]
    [InlineData("Ctrl+Shift")]
    [InlineData("Ctrl+G+H")]
    [InlineData("NotARealKey")]
    public void TryParse_InvalidStrings_FailWithReason(string text)
    {
        var ok = HotkeyParser.TryParse(text, out var definition, out var error);

        Assert.False(ok);
        Assert.False(definition.IsValid);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    /// <summary>解析 → 格式化 → 再解析必须回到同一个值，否则设置文件会越写越乱。</summary>
    [Theory]
    [InlineData("Ctrl+Shift+G")]
    [InlineData("F6")]
    [InlineData("Alt+Numpad5")]
    public void RoundTrip_PreservesDefinition(string text)
    {
        Assert.True(HotkeyParser.TryParse(text, out var first, out _));
        var serialized = first.ToConfigString();

        Assert.True(HotkeyParser.TryParse(serialized, out var second, out _));

        Assert.Equal(first.Modifiers, second.Modifiers);
        Assert.Equal(first.VirtualKey, second.VirtualKey);
    }

    [Fact]
    public void ToConfigString_UsesCanonicalModifierOrder()
    {
        Assert.True(HotkeyParser.TryParse("Shift+Ctrl+G", out var definition, out _));

        Assert.Equal("Ctrl+Shift+G", definition.ToConfigString());
    }

    /// <summary>MOD_NOREPEAT 必须始终带上：长按热键不该刷出一串截图。</summary>
    [Fact]
    public void RegistrationModifiers_AlwaysIncludeNoRepeat()
    {
        Assert.True(HotkeyParser.TryParse("F6", out var definition, out _));

        Assert.Equal(0x4000u, definition.RegistrationModifiers & 0x4000u);
    }

    [Fact]
    public void HotkeyDefinition_ReportsModifierFlags()
    {
        Assert.True(HotkeyParser.TryParse("Ctrl+Alt+G", out var definition, out _));

        Assert.True(definition.HasControl);
        Assert.True(definition.HasAlt);
        Assert.False(definition.HasShift);
        Assert.False(definition.HasWin);
        Assert.True(definition.IsValid);
    }

    [Fact]
    public void Defaults_AreAsConfigured()
    {
        var defaults = MortarHUD.Core.Configuration.HotkeySettings.CreateDefault();

        Assert.Equal("F6", defaults.CaptureGun);

        // 记录目标默认用鼠标中键：记录目标本来就是鼠标指着地图的动作，
        // 而且中键在游戏里通常另有用途的概率最低。
        Assert.Equal("MouseMiddle", defaults.CaptureTarget);

        Assert.Equal("F8", defaults.ToggleHud);
        Assert.Equal("F9", defaults.OpenSettings);
    }

    [Theory]
    [InlineData("MouseMiddle", 3u, 0u)]
    [InlineData("MouseLeft", 1u, 0u)]
    [InlineData("MouseX1", 4u, 0u)]
    [InlineData("Ctrl+MouseMiddle", 3u, 0x0002u)]
    [InlineData("Alt+Shift+MouseRight", 2u, 0x0001u | 0x0004u)]
    public void TryParse_MouseButtons_Succeed(string text, uint expectedButton, uint expectedModifiers)
    {
        var ok = HotkeyParser.TryParse(text, out var definition, out var error);

        Assert.True(ok, error);
        Assert.True(definition.IsMouse);
        Assert.Equal(expectedButton, definition.VirtualKey);
        Assert.Equal(expectedModifiers, definition.Modifiers);
    }

    /// <summary>键盘键不能被误判成鼠标键，反之亦然。</summary>
    [Fact]
    public void TryParse_KeyboardKey_IsNotMarkedAsMouse()
    {
        Assert.True(HotkeyParser.TryParse("F6", out var definition, out _));

        Assert.False(definition.IsMouse);
    }

    [Fact]
    public void RoundTrip_PreservesMouseBinding()
    {
        Assert.True(HotkeyParser.TryParse("Ctrl+MouseMiddle", out var first, out _));

        Assert.True(HotkeyParser.TryParse(first.ToConfigString(), out var second, out _));

        Assert.Equal(first, second);
        Assert.True(second.IsMouse);
    }

    /// <summary>界面上要显示中文名，而不是 MouseMiddle 这种配置写法。</summary>
    [Fact]
    public void ToDisplayString_UsesChineseForMouseButtons()
    {
        Assert.True(HotkeyParser.TryParse("MouseMiddle", out var definition, out _));

        Assert.Equal("鼠标中键", definition.ToDisplayString());
        Assert.Equal("MouseMiddle", definition.ToConfigString());
    }
}
