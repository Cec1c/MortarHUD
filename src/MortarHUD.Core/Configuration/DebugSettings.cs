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

    /// <summary>
    /// 采样模式：每次采集额外保存一张整屏截图和当时的光标位置。
    /// </summary>
    /// <remarks>
    /// ROI 里混进地图网格线之类的干扰时，光看 ROI 推不出「文字到底在光标哪个方位」——
    /// 只有整屏 + 光标位置这一对，才能事后把文字坐标量准，用来标定 ROI 的默认值。
    /// </remarks>
    public bool SaveFullFrame { get; set; }

    public DebugSettings Clone() => (DebugSettings)MemberwiseClone();
}
