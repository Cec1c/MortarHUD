namespace MortarHUD.Core.Configuration;

/// <summary>General 页设置（TDD §28）。</summary>
public sealed class GeneralSettings
{
    public bool StartMinimized { get; set; }

    public bool StartWithWindows { get; set; }

    public bool MinimizeToTray { get; set; } = true;

    /// <summary>每个坐标单位对应的米数。</summary>
    public double MetersPerCoordinateUnit { get; set; } = 100.0;

    /// <summary>+X 方向："East" 或 "West"。</summary>
    public string XPositiveDirection { get; set; } = "East";

    /// <summary>+Y 方向："North" 或 "South"。</summary>
    public string YPositiveDirection { get; set; } = "North";

    public string Language { get; set; } = "zh-CN";

    public GeneralSettings Clone() => (GeneralSettings)MemberwiseClone();
}
