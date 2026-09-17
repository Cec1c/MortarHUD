namespace MortarHUD.Core.Configuration;

/// <summary>Debug 开关（TDD §33）。默认全部关闭。</summary>
public sealed class DebugSettings
{
    public bool Enabled { get; set; }

    public bool ShowRoiRectangle { get; set; }

    public bool ShowCursorAnchor { get; set; }

    public bool ShowRawOcrText { get; set; }

    public bool ShowParsedCoordinates { get; set; }

    public bool ShowConfidence { get; set; }

    public bool ShowTiming { get; set; }

    public bool SaveRawRoi { get; set; }

    public bool SaveProcessedRoi { get; set; }

    public DebugSettings Clone() => (DebugSettings)MemberwiseClone();
}
