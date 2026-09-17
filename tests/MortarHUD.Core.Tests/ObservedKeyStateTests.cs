using MortarHUD.Platform.Windows.Hotkeys;
using Xunit;

namespace MortarHUD.Core.Tests;

public class ObservedKeyStateTests
{
    [Fact]
    public void ReleaseAndRepeatDoNotRestartCalibration()
    {
        var state = new ObservedKeyState();
        Assert.True(state.Press(IntPtr.Zero, 77, 0));
        Assert.False(state.Press(IntPtr.Zero, 77, 0));
        Assert.False(state.Press(IntPtr.Zero, 77, 1));
        Assert.True(state.Press(IntPtr.Zero, 77, 0));
    }

    [Fact]
    public void ExtendedBreakAndSeparateDevicesAreHandled()
    {
        var state = new ObservedKeyState();
        Assert.True(state.Press(new IntPtr(1), 77, 2));
        Assert.True(state.Press(new IntPtr(2), 77, 2));
        Assert.False(state.Press(new IntPtr(1), 77, 3));
        Assert.False(state.Press(new IntPtr(2), 77, 2));
        state.Clear();
        Assert.True(state.Press(new IntPtr(2), 77, 2));
        Assert.False(state.Press(IntPtr.Zero, 255, 0));
    }
}
