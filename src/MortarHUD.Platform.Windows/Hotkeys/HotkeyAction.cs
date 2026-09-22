namespace MortarHUD.Platform.Windows.Hotkeys;

/// <summary>热键要触发的功能（TDD §18 / §29）。</summary>
public enum HotkeyAction
{
    CaptureGun,
    CaptureTarget,
    ToggleHud,
    OpenSettings,

    /// <summary>
    /// 按下「打开地图」键时自动校准炮位。
    /// </summary>
    /// <remarks>
    /// 与其他动作不同，这个走<b>观察模式</b>：游戏必须先收到这个键才能打开地图，
    /// 所以绝不能用 RegisterHotKey 截住它。
    /// </remarks>
    AutoCalibrateGun,
    ToggleRuler,
}

public sealed class HotkeyPressedEventArgs : EventArgs
{
    public HotkeyPressedEventArgs(HotkeyAction action, HotkeyDefinition definition)
    {
        Action = action;
        Definition = definition;
    }

    public HotkeyAction Action { get; }

    public HotkeyDefinition Definition { get; }
}

/// <summary>一次注册尝试的结果。</summary>
public sealed record HotkeyRegistrationResult(
    HotkeyAction Action,
    HotkeyDefinition Definition,
    bool Success,
    string? Error)
{
    public static HotkeyRegistrationResult Ok(HotkeyAction action, HotkeyDefinition definition)
        => new(action, definition, true, null);

    public static HotkeyRegistrationResult Failed(HotkeyAction action, HotkeyDefinition definition, string error)
        => new(action, definition, false, error);
}
