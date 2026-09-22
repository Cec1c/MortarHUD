using MortarHUD.Core.Models;

namespace MortarHUD.Core.Session;

/// <summary>开图动画可能短暂显示上次的坐标，单张看似正确的读数不足以记录炮位。</summary>
public sealed class AutoCalibrationConsensus
{
    public const int FrameIntervalMs = 60;
    private MapCoordinate? _previous;

    public bool Observe(MapCoordinate? coordinate)
    {
        var agreed = coordinate is { } current && _previous is { } previous
            && Math.Abs(current.X - previous.X) < .005 && Math.Abs(current.Y - previous.Y) < .005;
        _previous = coordinate;
        return agreed;
    }
}
