using MortarHUD.Core.Ballistics;
using MortarHUD.Core.Configuration;
using Xunit;

namespace MortarHUD.Core.Tests;

public class SightRulerTests
{
    [Fact]
    public void Target340CentersThe340MarkAndSpacesAdjacentMilMarks102Pixels()
    {
        var marks = SightRuler.Build(340, new RulerProfile());
        Assert.Equal(540, marks.Single(t => t.RangeMeters == 340).Y);
        Assert.Equal(438, marks.Single(t => t.RangeMeters == 385).Y);
        Assert.Equal(642, marks.Single(t => t.RangeMeters == 290).Y);
        Assert.Equal(744, marks.Single(t => t.RangeMeters == 240).Y);
    }

    [Fact]
    public void For327MetersTheGame340MarkMustBeAboveTheCenter()
    {
        var mark = SightRuler.Build(327, new RulerProfile()).Single(t => t.RangeMeters == 340);
        Assert.Equal(513.48, mark.Y, 6);
        Assert.True(mark.IsReference);
    }

    [Theory]
    [InlineData(80, 950)]
    [InlineData(110, 900)]
    [InlineData(132, 850)]
    [InlineData(340, 650)]
    [InlineData(510, 450)]
    [InlineData(684, 150)]
    public void SixScreenshotsProvideMeasuredDistanceMilAnchors(double range, double expectedMil)
    {
        Assert.True(SightRuler.TryGetMil(range, out var mil));
        Assert.Equal(expectedMil, mil);
    }

    [Theory]
    [InlineData(79.99)]
    [InlineData(684.01)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void UnmeasuredRangesNeverProduceAnAimingScale(double range)
    {
        Assert.False(SightRuler.TryGetMil(range, out _));
        Assert.Empty(SightRuler.Build(range, new RulerProfile()));
    }

    [Fact]
    public void DifferentAspectRatioKeepsTheSightOffsetRelativeToClientCenter()
    {
        var wide = RulerProfile.CreateDefault(3440, 1440);
        Assert.Equal(1392, wide.AxisX);
        Assert.Equal(720, wide.CenterY);
        Assert.Equal(136, wide.PixelsPer50Mil);
        Assert.Equal(584, SightRuler.Build(340, wide).Single(t => t.RangeMeters == 385).Y);
    }

    [Fact]
    public void SavedResolutionOverridesDefaultsAndCloneDoesNotEditTheOriginal()
    {
        var settings = new RulerSettings();
        var custom = RulerProfile.CreateDefault(2560, 1440);
        custom.CenterY = 710;
        custom.PixelsPer50Mil = 140;
        settings.Profiles.Add(custom);
        var clone = settings.Clone();
        clone.Profiles[1].CenterY = 700;
        Assert.Equal(710, settings.Resolve(2560, 1440).CenterY);
        Assert.Equal(540, settings.Resolve(1920, 1080).CenterY);
        Assert.Equal(140, settings.Resolve(2560, 1440).CenterY - SightRuler.Build(340, settings.Resolve(2560, 1440)).Single(t => t.RangeMeters == 385).Y, 6);
    }

    [Fact]
    public void InvalidCalibrationCannotDrawAnApparentlyValidRuler()
    {
        Assert.Empty(SightRuler.Build(340, new RulerProfile { PixelsPer50Mil = double.NaN }));
        Assert.False(new RulerProfile { CenterY = 1200 }.IsValid);
    }
}
