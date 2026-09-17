namespace MortarHUD.Core.Configuration;

/// <summary>
/// 鼠标相对 ROI（TDD §10.2）。
/// </summary>
/// <remarks>
/// <para>
/// 默认值不是 TDD 初稿里的 <c>+10, -80, 240x140</c>，而是根据仓库里三张实机截图反推出来的。
/// 测量方法（三图交叉验证）：坐标读数以光标为锚点，<c>y</c> 行左上角约为
/// 光标 <c>+(21, -57)</c>，<c>x</c> 行再往右下各偏 <c>+(25, +55)</c>；整块文本约 70x67 px。
/// 因此这里取一个把整块文本包住、并留出 ±30px 容错的窗口。
/// </para>
/// <para>
/// TDD §10.2 明确要求「实际默认值必须根据截图 Benchmark 调整」，此即调整结果。
/// </para>
/// </remarks>
public sealed class RoiSettings
{
    /// <summary>与 1920x1080 参考分辨率对应的默认偏移。</summary>
    public const int ReferenceOffsetX = -15;
    public const int ReferenceOffsetY = -90;
    public const int ReferenceWidth = 150;
    public const int ReferenceHeight = 140;

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
