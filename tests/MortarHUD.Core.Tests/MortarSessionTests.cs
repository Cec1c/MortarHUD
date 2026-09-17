using MortarHUD.Core.Ballistics;
using MortarHUD.Core.Models;
using MortarHUD.Core.Session;
using Xunit;

namespace MortarHUD.Core.Tests;

/// <summary>状态机测试（TDD §37 / §38）。</summary>
public class MortarSessionTests
{
    private static MortarSession CreateSession() => new(new MortarCalculator(100.0));

    [Fact]
    public void NewSession_StartsWithoutGun()
    {
        var session = CreateSession();

        Assert.Equal(MortarState.NoGun, session.State);
        Assert.Null(session.Gun);
        Assert.Null(session.Target);
    }

    [Fact]
    public void LockGun_MovesToGunLocked()
    {
        var session = CreateSession();

        session.LockGun(new MapCoordinate(98.09, 109.78));

        Assert.Equal(MortarState.GunLocked, session.State);
        Assert.Equal(MortarStatusKind.GunLocked, session.LastStatus);
    }

    [Fact]
    public void LockTarget_AfterGun_ComputesSolution()
    {
        var session = CreateSession();
        session.LockGun(new MapCoordinate(98.09, 109.78));

        var accepted = session.LockTarget(new MapCoordinate(99.58, 110.07));

        Assert.True(accepted);
        Assert.Equal(MortarState.TargetLocked, session.State);
        Assert.Equal(151.8, session.Solution.RangeMeters, precision: 1);
        Assert.Equal(79.0, session.Solution.BearingDegrees, precision: 1);
    }

    /// <summary>TDD §37：没有炮位时按下记录目标，必须提示且<strong>不得计算</strong>。</summary>
    [Fact]
    public void LockTarget_WithoutGun_RefusesAndReports()
    {
        var session = CreateSession();

        var accepted = session.LockTarget(new MapCoordinate(99.58, 110.07));

        Assert.False(accepted);
        Assert.Equal(MortarState.NoGun, session.State);
        Assert.Equal(MortarStatusKind.NoGunPosition, session.LastStatus);
        Assert.Null(session.Target);
        Assert.Equal(MortarSolution.Empty, session.Solution);
    }

    /// <summary>TDD §16 / §38：OCR 失败必须保持原目标不变。</summary>
    [Fact]
    public void ReportFailure_KeepsExistingTargetAndSolution()
    {
        var session = CreateSession();
        session.LockGun(new MapCoordinate(98.09, 109.78));
        session.LockTarget(new MapCoordinate(99.58, 110.07));

        var solutionBefore = session.Solution;
        var targetBefore = session.Target;

        session.ReportFailure(MortarStatusKind.OcrFailed, "X_NOT_FOUND");

        Assert.Equal(targetBefore, session.Target);
        Assert.Equal(solutionBefore, session.Solution);
        Assert.Equal(MortarState.TargetLocked, session.State);
        Assert.Equal(MortarStatusKind.OcrFailed, session.LastStatus);
        Assert.Equal("X_NOT_FOUND", session.LastError);
    }

    /// <summary>连续记录目标时，后一次覆盖前一次并重算。</summary>
    [Fact]
    public void LockTarget_Twice_UpdatesSolution()
    {
        var session = CreateSession();
        session.LockGun(new MapCoordinate(0, 0));
        session.LockTarget(new MapCoordinate(0, 1));

        Assert.Equal(0.0, session.Solution.BearingDegrees, precision: 9);

        session.LockTarget(new MapCoordinate(1, 0));

        Assert.Equal(90.0, session.Solution.BearingDegrees, precision: 9);
    }

    /// <summary>
    /// 换炮位之后旧目标的解算结果必须立刻重算——
    /// 否则 HUD 上会挂着一个基于旧炮位的方位角，那是最危险的一类错误。
    /// </summary>
    [Fact]
    public void LockGun_AfterTarget_RecomputesSolution()
    {
        var session = CreateSession();
        session.LockGun(new MapCoordinate(0, 0));
        session.LockTarget(new MapCoordinate(0, 10));

        Assert.Equal(0.0, session.Solution.BearingDegrees, precision: 9);

        session.LockGun(new MapCoordinate(10, 10));

        // 新炮位在目标正东 10 单位处，因此方位角变成正西 270°。
        Assert.Equal(270.0, session.Solution.BearingDegrees, precision: 9);
        Assert.Equal(MortarState.TargetLocked, session.State);
    }

    [Fact]
    public void ClearStatus_KeepsEverythingElse()
    {
        var session = CreateSession();
        session.LockGun(new MapCoordinate(1, 1));

        session.ClearStatus();

        Assert.Equal(MortarStatusKind.None, session.LastStatus);
        Assert.NotNull(session.Gun);
        Assert.Equal(MortarState.GunLocked, session.State);
    }

    [Fact]
    public void ClearTarget_FallsBackToGunLocked()
    {
        var session = CreateSession();
        session.LockGun(new MapCoordinate(1, 1));
        session.LockTarget(new MapCoordinate(2, 2));

        session.ClearTarget();

        Assert.Equal(MortarState.GunLocked, session.State);
        Assert.Equal(MortarSolution.Empty, session.Solution);
    }

    /// <summary>
    /// TDD §1.1：炮位保存后，即使游戏里自己的地图箭头消失也不影响使用。
    /// 这条在代码里的体现就是「炮位是一个独立的缓存值，不依赖任何实时数据」。
    /// </summary>
    [Fact]
    public void Gun_IsCachedIndependentlyOfAnyLiveReading()
    {
        var session = CreateSession();
        session.LockGun(new MapCoordinate(98.09, 109.78));

        // 中间可以发生任意多次失败的采集，炮位都不受影响。
        for (var i = 0; i < 5; i++)
        {
            session.ReportFailure(MortarStatusKind.OcrFailed, "X_NOT_FOUND");
        }

        Assert.Equal(new MapCoordinate(98.09, 109.78), session.Gun);
    }
}
