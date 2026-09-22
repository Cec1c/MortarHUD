using MortarHUD.Core.Configuration;

namespace MortarHUD.Core.Ballistics;

public readonly record struct SightCalibration(double RangeMeters, double Mil);
public readonly record struct SightTick(double RangeMeters, double Y, bool IsReference);

/// <summary>只复刻截图中的刻度表，不模拟弹道；视场或武器变化必须重新校准。</summary>
public static class SightRuler
{
    // 20260922010124_1 起六张 1080p 截图逐项对照左 RNG 和右 MIL。
    // 132→110 的间距同样为 50 MIL，不能把低距离段补成等米距。
    public static IReadOnlyList<SightCalibration> Calibration { get; } = Array.AsReadOnly<SightCalibration>(
    [
        new(80, 950), new(110, 900), new(132, 850), new(187, 800), new(240, 750),
        new(290, 700), new(340, 650), new(385, 600), new(430, 550), new(470, 500),
        new(510, 450), new(545, 400), new(578, 350), new(609, 300), new(637, 250),
        new(661, 200), new(684, 150),
    ]);

    public static bool TryGetMil(double range, out double mil)
    {
        mil = 0;
        if (!double.IsFinite(range) || range < Calibration[0].RangeMeters || range > Calibration[^1].RangeMeters) return false;
        for (var i = 1; i < Calibration.Count; i++)
        {
            var a = Calibration[i - 1];
            var b = Calibration[i];
            if (range > b.RangeMeters) continue;
            var ratio = (range - a.RangeMeters) / (b.RangeMeters - a.RangeMeters);
            mil = a.Mil + (b.Mil - a.Mil) * ratio;
            return true;
        }
        return false;
    }

    public static IReadOnlyList<SightTick> Build(double targetRange, RulerProfile profile)
    {
        if (!profile.IsValid || !TryGetMil(targetRange, out var targetMil)) return [];
        var ticks = new List<SightTick>();
        var halfHeight = 235 * profile.Height / 1080.0;
        void Add(double range, bool reference)
        {
            if (!TryGetMil(range, out var mil)) return;
            // 游戏 MIL 向下递增：目标小于 340m 时，340 刻线应在准星上方。
            var y = profile.CenterY + (mil - targetMil) * profile.PixelsPer50Mil / 50;
            if (y >= 24 && y <= profile.Height - 24 && Math.Abs(y - profile.CenterY) <= halfHeight)
                ticks.Add(new(range, y, reference));
        }
        foreach (var mark in Calibration) Add(mark.RangeMeters, true);
        for (var range = 80; range <= 680; range += 10)
            if (!Calibration.Any(m => m.RangeMeters == range)) Add(range, false);
        return ticks.OrderBy(t => t.Y).ToArray();
    }
}
