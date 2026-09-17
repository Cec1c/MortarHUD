namespace MortarHUD.Core.Configuration;

/// <summary>
/// 鼠标相对 ROI（TDD §10.2）。
/// </summary>
/// <remarks>
/// <para>
/// 默认值按 1920x1080 实机标注标定：<c>y</c> 行左上角在光标 <c>+(18, -61)</c>，
/// 整块读数（<c>y</c> 行 + <c>x</c> 行）约 75x72，两行相距 55px、水平缩进 25px。
/// 这里取一个把整块文字包住并留 ±12px 余量的窗口。
/// </para>
/// <para>
/// **不要为了「容错」再把它放大**：地图网格线会穿过 ROI，它是半透明白，
/// 比坐标文字还亮，二值化后变成粗黑块，与 y、x 两个轴字母粘连，直接把识别结果
/// 啃成 "10.29" 这种缺了轴字母的残片。ROI 越小，网格线能造成的破坏面积越小——
/// 原来那个 150x140 的窗口就是栽在这一点上。
/// </para>
/// </remarks>
public sealed class RoiSettings
{
    /// <summary>与 1920x1080 参考分辨率对应的默认偏移。</summary>
    public const int ReferenceOffsetX = 5;
    public const int ReferenceOffsetY = -74;
    public const int ReferenceWidth = 100;
    public const int ReferenceHeight = 96;

    public int Width { get; set; } = ReferenceWidth;

    public int Height { get; set; } = ReferenceHeight;

    public int OffsetX { get; set; } = ReferenceOffsetX;

    public int OffsetY { get; set; } = ReferenceOffsetY;

    /// <summary>
    /// 分辨率缩放系数。1.0 = 按 1920x1080 的实测像素直接用。
    /// </summary>
    public double Scale { get; set; } = 1.0;

    /// <summary>
    /// 开启后按主屏高度 / 1080 自动推算 <see cref="Scale"/>，
    /// 让不同分辨率下的 ROI 都覆盖到同样比例的 UI 区域。
    /// </summary>
    public bool AutoScale { get; set; } = true;

    public RoiSettings Clone() => new()
    {
        Width = Width,
        Height = Height,
        OffsetX = OffsetX,
        OffsetY = OffsetY,
        Scale = Scale,
        AutoScale = AutoScale,
    };
}
