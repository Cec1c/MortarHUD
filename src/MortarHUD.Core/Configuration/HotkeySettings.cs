namespace MortarHUD.Core.Configuration;

/// <summary>
/// 全局热键绑定。字符串形式（如 "F6"、"Ctrl+Shift+G"）便于直接写进 settings.json。
/// </summary>
public sealed class HotkeySettings
{
    public string CaptureGun { get; set; } = "F6";

    /// <summary>
    /// 记录目标。默认用鼠标中键：这个键在游戏里通常没有别的用途，
    /// 而且记录目标本来就是「鼠标指着地图上某处」的动作，用鼠标键更顺手。
    /// 注意 RegisterHotKey 不支持鼠标键，它走的是Raw Input。
    /// </summary>
    public string CaptureTarget { get; set; } = "MouseMiddle";

    public string ToggleHud { get; set; } = "F8";

    public string OpenSettings { get; set; } = "F9";

    // ---- 按地图键自动校准炮位 ----
    //
    // 游戏里按 M 打开地图时，鼠标会复位到地图中心——也就是自己的位置。
    // 于是「按下 M」本身就等价于「把光标移到炮位上」，可以顺势自动记录炮位。
    //
    // 这个键<strong>不能</strong>用 RegisterHotKey 注册：那会把 M 键截住，
    // 游戏收不到，地图根本打不开。必须用Raw Input只观察不拦截。

    /// <summary>是否启用「按地图键自动校准炮位」。</summary>
    public bool AutoCalibrateEnabled { get; set; } = true;

    /// <summary>触发自动校准的按键。默认 M（游戏里打开地图的键）。</summary>
    public string AutoCalibrateKey { get; set; } = "M";

    /// <summary>
    /// 按下地图键之后等待多久再截图，单位毫秒。
    /// </summary>
    /// <remarks>
    /// 地图有打开动画，光标也不是瞬间复位的。等太短会截到复位前的画面，
    /// 等太久用户会感觉「迟钝」。150ms 起连续取样，避免等待 OCR 时炮位说明浮层已经盖住坐标；可调。
    /// </remarks>
    public int AutoCalibrateDelayMs { get; set; } = 150;

    /// <summary>一次开图的短时连拍数量，运行时限制在 2–8 帧。</summary>
    /// <remarks>
    /// 每隔 60ms 冻结一帧，再开始 OCR；连续两帧坐标一致才锁定。
    /// 不等前一帧 OCR 完成后重截，避免说明浮层出现后才拿到画面。
    /// </remarks>
    public int AutoCalibrateAttempts { get; set; } = 4;

    /// <summary>恢复 TDD §18 的出厂默认。</summary>
    public static HotkeySettings CreateDefault() => new();

    public HotkeySettings Clone() => new()
    {
        AutoCalibrateEnabled = AutoCalibrateEnabled,
        AutoCalibrateKey = AutoCalibrateKey,
        AutoCalibrateDelayMs = AutoCalibrateDelayMs,
        AutoCalibrateAttempts = AutoCalibrateAttempts,
        CaptureGun = CaptureGun,
        CaptureTarget = CaptureTarget,
        ToggleHud = ToggleHud,
        OpenSettings = OpenSettings,
    };
}
