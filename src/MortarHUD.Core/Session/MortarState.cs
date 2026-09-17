namespace MortarHUD.Core.Session;

/// <summary>内部状态机（TDD §37）。</summary>
public enum MortarState
{
    /// <summary>还没有炮位，无法解算。</summary>
    NoGun,

    /// <summary>炮位已锁定，等待目标。</summary>
    GunLocked,

    /// <summary>炮位与目标都已锁定，HUD 显示解算结果。</summary>
    TargetLocked,
}

/// <summary>
/// 短暂显示在 HUD 上的状态提示（TDD §20 的状态信息，会自动淡出）。
/// </summary>
public enum MortarStatusKind
{
    None,

    GunLocked,

    TargetLocked,

    /// <summary>OCR 失败。</summary>
    OcrFailed,

    /// <summary>没有炮位就按了记录目标。</summary>
    NoGunPosition,

    /// <summary>坐标超出配置范围。</summary>
    InvalidCoordinate,

    /// <summary>截图失败。</summary>
    CaptureFailed,
}
