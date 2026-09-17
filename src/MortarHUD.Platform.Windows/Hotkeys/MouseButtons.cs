namespace MortarHUD.Platform.Windows.Hotkeys;

/// <summary>
/// 可作为热键的鼠标键。
/// </summary>
/// <remarks>
/// 取值刻意避开虚拟键码的常规区间，并配合 <see cref="HotkeyDevice"/> 一起使用，
/// 这样 <see cref="HotkeyDefinition"/> 不需要为鼠标单独加一套字段。
/// </remarks>
public enum MouseButton
{
    Left = 1,
    Right = 2,
    Middle = 3,
    X1 = 4,
    X2 = 5,
}

/// <summary>热键来自键盘还是鼠标。</summary>
public enum HotkeyDevice
{
    Keyboard,
    Mouse,
}

public static class MouseButtonNames
{
    /// <summary>配置里使用的规范名。settings.json 存的就是这些。</summary>
    public static string GetName(MouseButton button) => button switch
    {
        MouseButton.Left => "MouseLeft",
        MouseButton.Right => "MouseRight",
        MouseButton.Middle => "MouseMiddle",
        MouseButton.X1 => "MouseX1",
        MouseButton.X2 => "MouseX2",
        _ => "MouseMiddle",
    };

    /// <summary>界面上显示的名字。</summary>
    public static string GetDisplayName(MouseButton button) => button switch
    {
        MouseButton.Left => "鼠标左键",
        MouseButton.Right => "鼠标右键",
        MouseButton.Middle => "鼠标中键",
        MouseButton.X1 => "鼠标侧键 1",
        MouseButton.X2 => "鼠标侧键 2",
        _ => "鼠标中键",
    };

    public static bool TryParse(string name, out MouseButton button)
    {
        switch (name.Trim().ToLowerInvariant())
        {
            case "mouseleft" or "lbutton" or "lmb" or "鼠标左键":
                button = MouseButton.Left;
                return true;

            case "mouseright" or "rbutton" or "rmb" or "鼠标右键":
                button = MouseButton.Right;
                return true;

            case "mousemiddle" or "mbutton" or "mmb" or "middle" or "鼠标中键" or "中键":
                button = MouseButton.Middle;
                return true;

            case "mousex1" or "xbutton1" or "mouse4" or "鼠标侧键1":
                button = MouseButton.X1;
                return true;

            case "mousex2" or "xbutton2" or "mouse5" or "鼠标侧键2":
                button = MouseButton.X2;
                return true;

            default:
                button = MouseButton.Middle;
                return false;
        }
    }

    /// <summary>全部可选的鼠标键，供设置界面的下拉框使用。</summary>
    public static IReadOnlyList<MouseButton> All { get; } =
    [
        MouseButton.Left,
        MouseButton.Right,
        MouseButton.Middle,
        MouseButton.X1,
        MouseButton.X2,
    ];
}
