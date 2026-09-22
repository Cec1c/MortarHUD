using MortarHUD.Core.Models;
using MortarHUD.Core.Session;
using Xunit;

namespace MortarHUD.Core.Tests;

public class AutoCalibrationConsensusTests
{
    [Fact]
    public void TransitionCoordinateIsNotEnoughButTwoFollowingStableFramesAre()
    {
        var consensus = new AutoCalibrationConsensus();
        Assert.False(consensus.Observe(new(98.82, 110.44)));
        Assert.False(consensus.Observe(new(98.49, 110.38)));
        Assert.True(consensus.Observe(new(98.49, 110.38)));
    }

    [Fact]
    public void TooltipOrMissingCenterBreaksTheConsecutiveFrameRequirement()
    {
        var consensus = new AutoCalibrationConsensus();
        var gun = new MapCoordinate(98.49, 110.38);
        Assert.False(consensus.Observe(gun));
        Assert.False(consensus.Observe(null));
        Assert.False(consensus.Observe(gun));
        Assert.True(consensus.Observe(gun));
    }
}
