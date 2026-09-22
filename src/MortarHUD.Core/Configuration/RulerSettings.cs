namespace MortarHUD.Core.Configuration;

public sealed class RulerSettings
{
    public bool Enabled { get; set; }
    public string ToggleHotkey { get; set; } = "F10";
    public List<RulerProfile> Profiles { get; set; } = [RulerProfile.CreateDefault(1920, 1080)];

    public RulerProfile Resolve(int width, int height)
    {
        var saved = Profiles?.FirstOrDefault(p => p.Width == width && p.Height == height && p.IsValid);
        return saved?.Clone() ?? RulerProfile.CreateDefault(width, height);
    }

    public RulerSettings Clone() => new()
    {
        Enabled = Enabled, ToggleHotkey = ToggleHotkey,
        Profiles = (Profiles ?? []).Select(p => p.Clone()).ToList(),
    };
}

/// <summary>全部是游戏客户区内的物理像素，Windows DPI 不参与标定。</summary>
public sealed class RulerProfile
{
    public int Width { get; set; } = 1920;
    public int Height { get; set; } = 1080;
    public double AxisX { get; set; } = 714;
    public double CenterY { get; set; } = 540;
    public double PixelsPer50Mil { get; set; } = 102;
    [System.Text.Json.Serialization.JsonIgnore]
    public string DisplayName => $"{Width} × {Height}";
    public override string ToString() => DisplayName;
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsValid => Width >= 640 && Height >= 480 && Width <= 16384 && Height <= 16384
        && double.IsFinite(AxisX) && AxisX >= 0 && AxisX <= Width
        && double.IsFinite(CenterY) && CenterY >= 0 && CenterY <= Height
        && double.IsFinite(PixelsPer50Mil) && PixelsPer50Mil >= 10 && PixelsPer50Mil <= Height;

    public static RulerProfile CreateDefault(int width, int height) => new()
    {
        Width = width, Height = height, AxisX = width / 2.0 - 246 * height / 1080.0,
        CenterY = height / 2.0, PixelsPer50Mil = 102 * height / 1080.0,
    };
    public RulerProfile Clone() => (RulerProfile)MemberwiseClone();
}
