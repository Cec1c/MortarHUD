using MortarHUD.Core.Ballistics;
using MortarHUD.Core.Models;

namespace MortarHUD.Core.Session;

/// <summary>
/// 保存炮位 A、目标 B 与当前解算结果，并维护 <see cref="MortarState"/>。
/// </summary>
/// <remarks>
/// 关键约束（TDD §16 / §38）：任何一次失败都<strong>不得</strong>污染已有数据。
/// OCR 失败时目标保持原样，界面必须能明确告诉用户「目标没变」。
/// </remarks>
public sealed class MortarSession
{
    private readonly IMortarCalculator _calculator;

    public MortarSession(IMortarCalculator calculator)
        => _calculator = calculator ?? throw new ArgumentNullException(nameof(calculator));

    public MapCoordinate? Gun { get; private set; }

    public MapCoordinate? Target { get; private set; }

    public MortarSolution Solution { get; private set; } = MortarSolution.Empty;

    public MortarState State { get; private set; } = MortarState.NoGun;

    /// <summary>最近一次操作的提示。UI 负责让它淡出，状态本身不受影响。</summary>
    public MortarStatusKind LastStatus { get; private set; } = MortarStatusKind.None;

    /// <summary>最近一次失败的原因，写日志与 Debug 面板用。</summary>
    public string? LastError { get; private set; }

    /// <summary>炮位锁定后，即使游戏里自己的地图箭头消失也不影响后续使用（TDD §1.1）。</summary>
    public void LockGun(MapCoordinate gun)
    {
        Gun = gun;
        LastError = null;

        // 换炮位后旧目标的解算结果已经失效，立即重算。
        if (Target is { } target)
        {
            Solution = _calculator.Solve(gun, target);
            State = MortarState.TargetLocked;
        }
        else
        {
            Solution = MortarSolution.Empty;
            State = MortarState.GunLocked;
        }

        LastStatus = MortarStatusKind.GunLocked;
    }

    /// <summary>
    /// 锁定目标。没有炮位时拒绝计算（TDD §37：不得计算）。
    /// </summary>
    /// <returns>true 表示目标已更新。</returns>
    public bool LockTarget(MapCoordinate target)
    {
        if (Gun is not { } gun)
        {
            LastStatus = MortarStatusKind.NoGunPosition;
            LastError = "NO_GUN_POSITION";
            return false;
        }

        Target = target;
        Solution = _calculator.Solve(gun, target);
        State = MortarState.TargetLocked;
        LastStatus = MortarStatusKind.TargetLocked;
        LastError = null;
        return true;
    }

    /// <summary>
    /// 记录一次失败。炮位、目标与解算结果全部保持不变。
    /// </summary>
    public void ReportFailure(MortarStatusKind status, string? error)
    {
        LastStatus = status;
        LastError = error;
    }

    /// <summary>把提示标记为已消费（UI 淡出后调用）。</summary>
    public void ClearStatus()
    {
        LastStatus = MortarStatusKind.None;
    }

    public void ClearTarget()
    {
        Target = null;
        Solution = MortarSolution.Empty;
        State = Gun is null ? MortarState.NoGun : MortarState.GunLocked;
        LastStatus = MortarStatusKind.None;
    }

    public void Reset()
    {
        Gun = null;
        Target = null;
        Solution = MortarSolution.Empty;
        State = MortarState.NoGun;
        LastStatus = MortarStatusKind.None;
        LastError = null;
    }
}
