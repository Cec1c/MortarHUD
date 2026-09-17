using System.Collections.ObjectModel;
using System.IO;
using MortarHUD.Core.Configuration;
using MortarHUD.Core.Themes;

namespace MortarHUD.App.ViewModels;

/// <summary>
/// 设置页的视图模型。
/// </summary>
/// <remarks>
/// <para>
/// 采用「先读进本地字段、改完再 <see cref="SaveTo"/> 写回」的方式，
/// 而不是直接绑到 <see cref="MortarHudSettings"/> 上。
/// 这样点「取消」就能干净回滚，实时预览也只需要监听本对象的变更。
/// </para>
/// <para>
/// 任何属性变化都会触发 <see cref="Changed"/>，窗口据此刷新 HUD 预览。
/// </para>
/// </remarks>
public sealed class SettingsViewModel : ObservableObject
{
    private readonly HudThemeStore _themeStore = new();

    // ---- General ----
    private bool _startMinimized;
    private bool _startWithWindows;
    private bool _minimizeToTray;
    private double _metersPerCoordinateUnit;
    private bool _xPositiveIsEast;
    private bool _yPositiveIsNorth;
    private string _language = "zh-CN";

    // ---- Hotkeys ----
    private string _captureGun = "F6";
    private string _captureTarget = "F7";
    private string _toggleHud = "F8";
    private string _openSettings = "F9";
    private bool _autoCalibrateEnabled = true;
    private string _autoCalibrateKey = "M";
    private int _autoCalibrateDelayMs = 350;

    // ---- HUD ----
    private string _fontFamily = "Cascadia Mono";
    private double _fontSize = 22;
    private string _fontWeight = "SemiBold";
    private bool _italic;
    private double _letterSpacing;
    private double _lineHeight = 1.15;
    private HudTextAlignment _alignment = HudTextAlignment.Left;
    private string _primaryColor = "#7CFF6B";
    private string _secondaryColor = "#B8FFAF";
    private string _successColor = "#7CFF6B";
    private string _warningColor = "#FFD866";
    private string _errorColor = "#FF6464";
    private OutlineWeight _outline = OutlineWeight.Thin;
    private string _outlineColor = "#80000000";
    private double _outlineThickness = 1.0;
    private ShadowMode _shadow = ShadowMode.Soft;
    private string _shadowColor = "#C0000000";
    private HudBackgroundMode _background = HudBackgroundMode.None;
    private string _backgroundColor = "#80000000";
    private double _backgroundOpacity = 0.5;
    private double _cornerRadius = 6;
    private double _padding = 8;
    private double _opacity = 1.0;
    private HudLayout _layout = HudLayout.Compact;
    private HudAnchor _anchor = HudAnchor.CenterLeft;
    private double _offsetX = 40;
    private double _offsetY;
    private bool _positionUnlocked;
    private bool _showAz = true;
    private bool _showRng = true;
    private bool _showGun;
    private bool _showTarget;
    private int _bearingDecimals = 1;
    private int _rangeDecimals;

    // ---- OCR ----
    private string _ocrEngine = OcrEngineNames.Auto;
    private string _preprocessor = PreprocessorNames.Auto;
    private int _roiWidth = RoiSettings.ReferenceWidth;
    private int _roiHeight = RoiSettings.ReferenceHeight;
    private int _roiOffsetX = RoiSettings.ReferenceOffsetX;
    private int _roiOffsetY = RoiSettings.ReferenceOffsetY;
    private bool _roiAutoScale = true;
    private double _roiScale = 1.0;
    private double _coordinateMin;
    private double _coordinateMax = 200;
    private double _minimumConfidence = 0.60;
    private string _tessdataPath = "";
    private string _ocrLanguage = "eng";
    private string _characterWhitelist = "0123456789xy.:-";
    private int _pageSegMode = 6;
    private bool _requireDecimalPoint = true;

    // ---- Debug ----
    private bool _debugEnabled;
    private bool _showRoiRectangle;
    private bool _showCursorAnchor;
    private bool _showRawOcrText;
    private bool _showParsedCoordinates;
    private bool _showConfidence;
    private bool _showTiming;
    private bool _saveRawRoi;
    private bool _saveProcessedRoi;
    private bool _saveFullFrame;

    // ---- 主题 ----
    private HudTheme? _selectedTheme;

    public SettingsViewModel(MortarHudSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        LoadFrom(settings);
        ReloadThemes();
    }

    /// <summary>任何设置变化都会触发，窗口据此刷新预览。</summary>
    public event EventHandler? Changed;

    /// <summary>当前正在编辑的 HUD 主题（就是 <see cref="MortarHudSettings.Hud"/> 里那一份）。</summary>
    public HudTheme WorkingTheme { get; private set; } = HudThemeLibrary.CreateDefaultGreen();

    public ObservableCollection<HudTheme> Themes { get; } = [];

    public static IReadOnlyList<string> AvailableFonts { get; } =
    [
        "Cascadia Mono", "Consolas", "Segoe UI", "Segoe UI Variable Display",
        "Bahnschrift", "Tahoma", "Verdana", "Arial", "Microsoft YaHei UI", "SimHei",
    ];

    public static IReadOnlyList<string> AvailableFontWeights { get; } =
        ["Light", "Normal", "Medium", "SemiBold", "Bold", "Black"];

    public static IReadOnlyList<string> AvailableEngines { get; } =
        [OcrEngineNames.Auto, OcrEngineNames.Tesseract, OcrEngineNames.Template];

    public static IReadOnlyList<string> AvailablePreprocessors { get; } =
        [PreprocessorNames.Auto, PreprocessorNames.A, PreprocessorNames.B, PreprocessorNames.C];

    public static IReadOnlyList<HudLayout> AvailableLayouts { get; } = Enum.GetValues<HudLayout>();

    public static IReadOnlyList<HudAnchor> AvailableAnchors { get; } = Enum.GetValues<HudAnchor>();

    public static IReadOnlyList<OutlineWeight> AvailableOutlines { get; } = Enum.GetValues<OutlineWeight>();

    public static IReadOnlyList<ShadowMode> AvailableShadows { get; } = Enum.GetValues<ShadowMode>();

    public static IReadOnlyList<HudBackgroundMode> AvailableBackgrounds { get; } = Enum.GetValues<HudBackgroundMode>();

    public static IReadOnlyList<HudTextAlignment> AvailableAlignments { get; } = Enum.GetValues<HudTextAlignment>();

    // ================================================================ 载入 / 保存

    public void LoadFrom(MortarHudSettings settings)
    {
        var g = settings.General;
        _startMinimized = g.StartMinimized;
        _startWithWindows = g.StartWithWindows;
        _minimizeToTray = g.MinimizeToTray;
        _metersPerCoordinateUnit = g.MetersPerCoordinateUnit;
        _xPositiveIsEast = !string.Equals(g.XPositiveDirection, "West", StringComparison.OrdinalIgnoreCase);
        _yPositiveIsNorth = !string.Equals(g.YPositiveDirection, "South", StringComparison.OrdinalIgnoreCase);
        _language = g.Language;

        _captureGun = settings.Hotkeys.CaptureGun;
        _captureTarget = settings.Hotkeys.CaptureTarget;
        _toggleHud = settings.Hotkeys.ToggleHud;
        _openSettings = settings.Hotkeys.OpenSettings;
        _autoCalibrateEnabled = settings.Hotkeys.AutoCalibrateEnabled;
        _autoCalibrateKey = settings.Hotkeys.AutoCalibrateKey;
        _autoCalibrateDelayMs = settings.Hotkeys.AutoCalibrateDelayMs;

        WorkingTheme = settings.Hud.CurrentTheme.Clone();

        var h = settings.Hud;
        _anchor = h.Anchor;
        _offsetX = h.OffsetX;
        _offsetY = h.OffsetY;
        _positionUnlocked = h.PositionUnlocked;
        _showAz = h.ShowAz;
        _showRng = h.ShowRng;
        _showGun = h.ShowGun;
        _showTarget = h.ShowTarget;
        _bearingDecimals = h.BearingDecimals;
        _rangeDecimals = h.RangeDecimals;
        ApplyThemeToFields(WorkingTheme);

        var o = settings.Ocr;
        _ocrEngine = o.Engine;
        _preprocessor = o.Preprocessor;
        _coordinateMin = o.CoordinateMin;
        _coordinateMax = o.CoordinateMax;
        _minimumConfidence = o.MinimumConfidence;
        _tessdataPath = o.TessdataPath;
        _ocrLanguage = o.Language;
        _characterWhitelist = o.CharacterWhitelist;
        _pageSegMode = o.PageSegMode;
        _requireDecimalPoint = o.RequireDecimalPoint;

        var r = settings.Roi;
        _roiWidth = r.Width;
        _roiHeight = r.Height;
        _roiOffsetX = r.OffsetX;
        _roiOffsetY = r.OffsetY;
        _roiAutoScale = r.AutoScale;
        _roiScale = r.Scale;

        var d = settings.Debug;
        _debugEnabled = d.Enabled;
        _showRoiRectangle = d.ShowRoiRectangle;
        _showCursorAnchor = d.ShowCursorAnchor;
        _showRawOcrText = d.ShowRawOcrText;
        _showParsedCoordinates = d.ShowParsedCoordinates;
        _showConfidence = d.ShowConfidence;
        _showTiming = d.ShowTiming;
        _saveRawRoi = d.SaveRawRoi;
        _saveProcessedRoi = d.SaveProcessedRoi;
        _saveFullFrame = d.SaveFullFrame;

        RaiseAllChanged();
    }

    public void SaveTo(MortarHudSettings settings)
    {
        var g = settings.General;
        g.StartMinimized = _startMinimized;
        g.StartWithWindows = _startWithWindows;
        g.MinimizeToTray = _minimizeToTray;
        g.MetersPerCoordinateUnit = _metersPerCoordinateUnit;
        g.XPositiveDirection = _xPositiveIsEast ? "East" : "West";
        g.YPositiveDirection = _yPositiveIsNorth ? "North" : "South";
        g.Language = _language;

        settings.Hotkeys.CaptureGun = _captureGun;
        settings.Hotkeys.CaptureTarget = _captureTarget;
        settings.Hotkeys.ToggleHud = _toggleHud;
        settings.Hotkeys.OpenSettings = _openSettings;
        settings.Hotkeys.AutoCalibrateEnabled = _autoCalibrateEnabled;
        settings.Hotkeys.AutoCalibrateKey = _autoCalibrateKey;
        settings.Hotkeys.AutoCalibrateDelayMs = _autoCalibrateDelayMs;

        CollectThemeFields(WorkingTheme);
        settings.Hud.CurrentTheme = WorkingTheme;
        settings.Hud.ActiveThemeName = WorkingTheme.Name;

        var h = settings.Hud;
        h.Anchor = _anchor;
        h.OffsetX = _offsetX;
        h.OffsetY = _offsetY;
        h.PositionUnlocked = _positionUnlocked;
        h.ShowAz = _showAz;
        h.ShowRng = _showRng;
        h.ShowGun = _showGun;
        h.ShowTarget = _showTarget;
        h.BearingDecimals = _bearingDecimals;
        h.RangeDecimals = _rangeDecimals;

        var o = settings.Ocr;
        o.Engine = _ocrEngine;
        o.Preprocessor = _preprocessor;
        o.CoordinateMin = _coordinateMin;
        o.CoordinateMax = _coordinateMax;
        o.MinimumConfidence = _minimumConfidence;
        o.TessdataPath = _tessdataPath;
        o.Language = _ocrLanguage;
        o.CharacterWhitelist = _characterWhitelist;
        o.PageSegMode = _pageSegMode;
        o.RequireDecimalPoint = _requireDecimalPoint;

        var r = settings.Roi;
        r.Width = _roiWidth;
        r.Height = _roiHeight;
        r.OffsetX = _roiOffsetX;
        r.OffsetY = _roiOffsetY;
        r.AutoScale = _roiAutoScale;
        r.Scale = _roiScale;

        var d = settings.Debug;
        d.Enabled = _debugEnabled;
        d.ShowRoiRectangle = _showRoiRectangle;
        d.ShowCursorAnchor = _showCursorAnchor;
        d.ShowRawOcrText = _showRawOcrText;
        d.ShowParsedCoordinates = _showParsedCoordinates;
        d.ShowConfidence = _showConfidence;
        d.ShowTiming = _showTiming;
        d.SaveRawRoi = _saveRawRoi;
        d.SaveProcessedRoi = _saveProcessedRoi;
        d.SaveFullFrame = _saveFullFrame;
    }

    private void ApplyThemeToFields(HudTheme theme)
    {
        _fontFamily = theme.FontFamily;
        _fontSize = theme.FontSize;
        _fontWeight = theme.FontWeight;
        _italic = theme.Italic;
        _letterSpacing = theme.LetterSpacing;
        _lineHeight = theme.LineHeight;
        _alignment = theme.Alignment;
        _primaryColor = theme.PrimaryColor;
        _secondaryColor = theme.SecondaryColor;
        _successColor = theme.SuccessColor;
        _warningColor = theme.WarningColor;
        _errorColor = theme.ErrorColor;
        _outline = theme.Outline;
        _outlineColor = theme.OutlineColor;
        _outlineThickness = theme.OutlineThickness;
        _shadow = theme.Shadow;
        _shadowColor = theme.ShadowColor;
        _background = theme.Background;
        _backgroundColor = theme.BackgroundColor;
        _backgroundOpacity = theme.BackgroundOpacity;
        _cornerRadius = theme.CornerRadius;
        _padding = theme.Padding;
        _opacity = theme.Opacity;
        _layout = theme.Layout;
    }

    private void CollectThemeFields(HudTheme theme)
    {
        theme.FontFamily = _fontFamily;
        theme.FontSize = _fontSize;
        theme.FontWeight = _fontWeight;
        theme.Italic = _italic;
        theme.LetterSpacing = _letterSpacing;
        theme.LineHeight = _lineHeight;
        theme.Alignment = _alignment;
        theme.PrimaryColor = _primaryColor;
        theme.SecondaryColor = _secondaryColor;
        theme.SuccessColor = _successColor;
        theme.WarningColor = _warningColor;
        theme.ErrorColor = _errorColor;
        theme.Outline = _outline;
        theme.OutlineColor = _outlineColor;
        theme.OutlineThickness = _outlineThickness;
        theme.Shadow = _shadow;
        theme.ShadowColor = _shadowColor;
        theme.Background = _background;
        theme.BackgroundColor = _backgroundColor;
        theme.BackgroundOpacity = _backgroundOpacity;
        theme.CornerRadius = _cornerRadius;
        theme.Padding = _padding;
        theme.Opacity = _opacity;
        theme.Layout = _layout;
    }

    private void RaiseAllChanged()
    {
        // 载入后整体刷新一次：逐个属性通知太啰嗦，而载入本来就是低频操作。
        foreach (var name in EnumeratePropertyNames())
        {
            RaisePropertyChanged(name);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static IEnumerable<string> EnumeratePropertyNames()
    {
        yield return nameof(StartMinimized);
        yield return nameof(FontFamily);
        yield return nameof(FontSize);
        yield return nameof(PrimaryColor);
        yield return nameof(Layout);
        yield return nameof(SelectedTheme);
        yield return nameof(WorkingTheme);
    }

    private void NotifyChanged()
    {
        // 字段改动统一在这里落到 WorkingTheme 上，任何一处改动都能立刻反映到预览。
        CollectThemeFields(WorkingTheme);
        SaveToWorkingSettings();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>把当前字段同步回一份临时设置对象，供预览直接使用。</summary>
    private MortarHudSettings? _previewSettings;

    public MortarHudSettings BuildPreviewSettings()
    {
        _previewSettings ??= new MortarHudSettings();
        SaveTo(_previewSettings);
        return _previewSettings;
    }

    private void SaveToWorkingSettings() => SaveTo(BuildPreviewSettings());

    // ================================================================ 属性

    public bool StartMinimized { get => _startMinimized; set { if (Set(ref _startMinimized, value)) NotifyChanged(); } }
    public bool StartWithWindows { get => _startWithWindows; set { if (Set(ref _startWithWindows, value)) NotifyChanged(); } }
    public bool MinimizeToTray { get => _minimizeToTray; set { if (Set(ref _minimizeToTray, value)) NotifyChanged(); } }
    public double MetersPerCoordinateUnit { get => _metersPerCoordinateUnit; set { if (Set(ref _metersPerCoordinateUnit, value)) NotifyChanged(); } }
    public bool XPositiveIsEast { get => _xPositiveIsEast; set { if (Set(ref _xPositiveIsEast, value)) NotifyChanged(); } }
    public bool YPositiveIsNorth { get => _yPositiveIsNorth; set { if (Set(ref _yPositiveIsNorth, value)) NotifyChanged(); } }
    public string Language { get => _language; set { if (Set(ref _language, value)) NotifyChanged(); } }

    public string CaptureGun { get => _captureGun; set { if (Set(ref _captureGun, value)) NotifyChanged(); } }
    public string CaptureTarget { get => _captureTarget; set { if (Set(ref _captureTarget, value)) NotifyChanged(); } }
    public string ToggleHudHotkey { get => _toggleHud; set { if (Set(ref _toggleHud, value)) NotifyChanged(); } }
    public string OpenSettingsHotkey { get => _openSettings; set { if (Set(ref _openSettings, value)) NotifyChanged(); } }

    public bool AutoCalibrateEnabled
    {
        get => _autoCalibrateEnabled;
        set { if (Set(ref _autoCalibrateEnabled, value)) NotifyChanged(); }
    }

    public string AutoCalibrateKey
    {
        get => _autoCalibrateKey;
        set { if (Set(ref _autoCalibrateKey, value)) NotifyChanged(); }
    }

    public int AutoCalibrateDelayMs
    {
        get => _autoCalibrateDelayMs;
        set { if (Set(ref _autoCalibrateDelayMs, value)) NotifyChanged(); }
    }

    public string FontFamily { get => _fontFamily; set { if (Set(ref _fontFamily, value)) NotifyChanged(); } }
    public double FontSize { get => _fontSize; set { if (Set(ref _fontSize, value)) NotifyChanged(); } }
    public string FontWeight { get => _fontWeight; set { if (Set(ref _fontWeight, value)) NotifyChanged(); } }
    public bool Italic { get => _italic; set { if (Set(ref _italic, value)) NotifyChanged(); } }
    public double LetterSpacing { get => _letterSpacing; set { if (Set(ref _letterSpacing, value)) NotifyChanged(); } }
    public double LineHeight { get => _lineHeight; set { if (Set(ref _lineHeight, value)) NotifyChanged(); } }
    public HudTextAlignment Alignment { get => _alignment; set { if (Set(ref _alignment, value)) NotifyChanged(); } }
    public string PrimaryColor { get => _primaryColor; set { if (Set(ref _primaryColor, value)) NotifyChanged(); } }
    public string SecondaryColor { get => _secondaryColor; set { if (Set(ref _secondaryColor, value)) NotifyChanged(); } }
    public string SuccessColor { get => _successColor; set { if (Set(ref _successColor, value)) NotifyChanged(); } }
    public string WarningColor { get => _warningColor; set { if (Set(ref _warningColor, value)) NotifyChanged(); } }
    public string ErrorColor { get => _errorColor; set { if (Set(ref _errorColor, value)) NotifyChanged(); } }
    public OutlineWeight Outline { get => _outline; set { if (Set(ref _outline, value)) NotifyChanged(); } }
    public string OutlineColor { get => _outlineColor; set { if (Set(ref _outlineColor, value)) NotifyChanged(); } }
    public double OutlineThickness { get => _outlineThickness; set { if (Set(ref _outlineThickness, value)) NotifyChanged(); } }
    public ShadowMode Shadow { get => _shadow; set { if (Set(ref _shadow, value)) NotifyChanged(); } }
    public string ShadowColor { get => _shadowColor; set { if (Set(ref _shadowColor, value)) NotifyChanged(); } }
    public HudBackgroundMode Background { get => _background; set { if (Set(ref _background, value)) NotifyChanged(); } }
    public string BackgroundColor { get => _backgroundColor; set { if (Set(ref _backgroundColor, value)) NotifyChanged(); } }
    public double BackgroundOpacity { get => _backgroundOpacity; set { if (Set(ref _backgroundOpacity, value)) NotifyChanged(); } }
    public double CornerRadius { get => _cornerRadius; set { if (Set(ref _cornerRadius, value)) NotifyChanged(); } }
    public double Padding { get => _padding; set { if (Set(ref _padding, value)) NotifyChanged(); } }
    public double Opacity { get => _opacity; set { if (Set(ref _opacity, value)) NotifyChanged(); } }
    public HudLayout Layout { get => _layout; set { if (Set(ref _layout, value)) NotifyChanged(); } }
    public HudAnchor Anchor { get => _anchor; set { if (Set(ref _anchor, value)) NotifyChanged(); } }
    public double OffsetX { get => _offsetX; set { if (Set(ref _offsetX, value)) NotifyChanged(); } }
    public double OffsetY { get => _offsetY; set { if (Set(ref _offsetY, value)) NotifyChanged(); } }
    public bool PositionUnlocked { get => _positionUnlocked; set { if (Set(ref _positionUnlocked, value)) NotifyChanged(); } }
    public bool ShowAz { get => _showAz; set { if (Set(ref _showAz, value)) NotifyChanged(); } }
    public bool ShowRng { get => _showRng; set { if (Set(ref _showRng, value)) NotifyChanged(); } }
    public bool ShowGun { get => _showGun; set { if (Set(ref _showGun, value)) NotifyChanged(); } }
    public bool ShowTarget { get => _showTarget; set { if (Set(ref _showTarget, value)) NotifyChanged(); } }
    public int BearingDecimals { get => _bearingDecimals; set { if (Set(ref _bearingDecimals, value)) NotifyChanged(); } }
    public int RangeDecimals { get => _rangeDecimals; set { if (Set(ref _rangeDecimals, value)) NotifyChanged(); } }

    public string OcrEngine { get => _ocrEngine; set { if (Set(ref _ocrEngine, value)) NotifyChanged(); } }
    public string Preprocessor { get => _preprocessor; set { if (Set(ref _preprocessor, value)) NotifyChanged(); } }
    public int RoiWidth { get => _roiWidth; set { if (Set(ref _roiWidth, value)) NotifyChanged(); } }
    public int RoiHeight { get => _roiHeight; set { if (Set(ref _roiHeight, value)) NotifyChanged(); } }
    public int RoiOffsetX { get => _roiOffsetX; set { if (Set(ref _roiOffsetX, value)) NotifyChanged(); } }
    public int RoiOffsetY { get => _roiOffsetY; set { if (Set(ref _roiOffsetY, value)) NotifyChanged(); } }
    public bool RoiAutoScale { get => _roiAutoScale; set { if (Set(ref _roiAutoScale, value)) NotifyChanged(); } }
    public double RoiScale { get => _roiScale; set { if (Set(ref _roiScale, value)) NotifyChanged(); } }
    public double CoordinateMin { get => _coordinateMin; set { if (Set(ref _coordinateMin, value)) NotifyChanged(); } }
    public double CoordinateMax { get => _coordinateMax; set { if (Set(ref _coordinateMax, value)) NotifyChanged(); } }
    public double MinimumConfidence { get => _minimumConfidence; set { if (Set(ref _minimumConfidence, value)) NotifyChanged(); } }
    public string TessdataPath { get => _tessdataPath; set { if (Set(ref _tessdataPath, value)) NotifyChanged(); } }
    public string OcrLanguage { get => _ocrLanguage; set { if (Set(ref _ocrLanguage, value)) NotifyChanged(); } }
    public string CharacterWhitelist { get => _characterWhitelist; set { if (Set(ref _characterWhitelist, value)) NotifyChanged(); } }
    public int PageSegMode { get => _pageSegMode; set { if (Set(ref _pageSegMode, value)) NotifyChanged(); } }
    public bool RequireDecimalPoint { get => _requireDecimalPoint; set { if (Set(ref _requireDecimalPoint, value)) NotifyChanged(); } }

    public bool DebugEnabled { get => _debugEnabled; set { if (Set(ref _debugEnabled, value)) NotifyChanged(); } }
    public bool ShowRoiRectangle { get => _showRoiRectangle; set { if (Set(ref _showRoiRectangle, value)) NotifyChanged(); } }
    public bool ShowCursorAnchor { get => _showCursorAnchor; set { if (Set(ref _showCursorAnchor, value)) NotifyChanged(); } }
    public bool ShowRawOcrText { get => _showRawOcrText; set { if (Set(ref _showRawOcrText, value)) NotifyChanged(); } }
    public bool ShowParsedCoordinates { get => _showParsedCoordinates; set { if (Set(ref _showParsedCoordinates, value)) NotifyChanged(); } }
    public bool ShowConfidence { get => _showConfidence; set { if (Set(ref _showConfidence, value)) NotifyChanged(); } }
    public bool ShowTiming { get => _showTiming; set { if (Set(ref _showTiming, value)) NotifyChanged(); } }
    public bool SaveRawRoi { get => _saveRawRoi; set { if (Set(ref _saveRawRoi, value)) NotifyChanged(); } }
    public bool SaveProcessedRoi { get => _saveProcessedRoi; set { if (Set(ref _saveProcessedRoi, value)) NotifyChanged(); } }

    /// <summary>每次采集额外保存整屏 + 光标位置，供事后标定 ROI 默认值。</summary>
    public bool SaveFullFrame { get => _saveFullFrame; set { if (Set(ref _saveFullFrame, value)) NotifyChanged(); } }

    public HudTheme? SelectedTheme
    {
        get => _selectedTheme;
        set => Set(ref _selectedTheme, value);
    }

    // ================================================================ 主题操作

    public void ReloadThemes()
    {
        Themes.Clear();

        foreach (var theme in _themeStore.LoadAll())
        {
            Themes.Add(theme);
        }

        SelectedTheme = Themes.FirstOrDefault(t =>
                            string.Equals(t.Name, WorkingTheme.Name, StringComparison.OrdinalIgnoreCase))
                        ?? Themes.FirstOrDefault();
    }

    /// <summary>把选中主题的视觉设置应用到当前工作主题上。</summary>
    public void ApplySelectedTheme()
    {
        if (SelectedTheme is null)
        {
            return;
        }

        WorkingTheme = SelectedTheme.Clone();
        ApplyThemeToFields(WorkingTheme);
        RaiseAllChanged();
    }

    /// <summary>另存为自定义主题。重名时会自动加后缀，不覆盖已有主题。</summary>
    public HudTheme SaveAsNewTheme(string name)
    {
        var unique = MakeUniqueName(name);
        var copy = WorkingTheme.CloneAsCustom(unique);

        // 把当前界面上的改动一并写进去。
        CollectThemeFields(copy);
        copy.Name = unique;

        _themeStore.Save(copy);
        ReloadThemes();

        WorkingTheme = copy.Clone();
        SelectedTheme = Themes.FirstOrDefault(t =>
            string.Equals(t.Name, unique, StringComparison.OrdinalIgnoreCase));

        return copy;
    }

    public void OverwriteSelectedTheme()
    {
        if (SelectedTheme is null || SelectedTheme.IsBuiltIn)
        {
            return;
        }

        var updated = WorkingTheme.CloneAsCustom(SelectedTheme.Name);
        CollectThemeFields(updated);
        _themeStore.Save(updated);

        ReloadThemes();
    }

    /// <summary>删除自定义主题。内置主题会被 <see cref="HudThemeStore"/> 拒绝。</summary>
    public bool DeleteSelectedTheme(out string? error)
    {
        error = null;

        if (SelectedTheme is null)
        {
            error = "没有选中主题。";
            return false;
        }

        if (SelectedTheme.IsBuiltIn)
        {
            error = $"内置主题「{SelectedTheme.Name}」不能删除，可以先「另存为」再改。";
            return false;
        }

        try
        {
            _themeStore.Delete(SelectedTheme.Name);
            ReloadThemes();
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            error = ex.Message;
            return false;
        }
    }

    public bool RenameSelectedTheme(string newName, out string? error)
    {
        error = null;

        if (SelectedTheme is null)
        {
            error = "没有选中主题。";
            return false;
        }

        if (SelectedTheme.IsBuiltIn)
        {
            error = $"内置主题「{SelectedTheme.Name}」不能改名。";
            return false;
        }

        if (string.IsNullOrWhiteSpace(newName))
        {
            error = "主题名不能为空。";
            return false;
        }

        try
        {
            var renamed = WorkingTheme.CloneAsCustom(newName.Trim());
            CollectThemeFields(renamed);

            _themeStore.Save(renamed);
            _themeStore.Delete(SelectedTheme.Name);

            ReloadThemes();
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            error = ex.Message;
            return false;
        }
    }

    public bool ExportTheme(string targetPath, out string? error)
    {
        error = null;

        try
        {
            var copy = WorkingTheme.CloneAsCustom(WorkingTheme.Name);
            CollectThemeFields(copy);

            var json = System.Text.Json.JsonSerializer.Serialize(
                copy, HudThemeStore.CreateSerializerOptions());

            File.WriteAllText(targetPath, json);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = ex.Message;
            return false;
        }
    }

    public bool ImportTheme(string sourcePath, out string? error)
    {
        error = null;

        try
        {
            var json = File.ReadAllText(sourcePath);
            var theme = System.Text.Json.JsonSerializer.Deserialize<HudTheme>(
                json, HudThemeStore.CreateSerializerOptions());

            if (theme is null || string.IsNullOrWhiteSpace(theme.Name))
            {
                error = "文件里没有有效的主题。";
                return false;
            }

            theme.IsBuiltIn = false;
            theme.Name = MakeUniqueName(theme.Name);
            _themeStore.Save(theme);
            ReloadThemes();
            return true;
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or UnauthorizedAccessException)
        {
            error = ex.Message;
            return false;
        }
    }

    private string MakeUniqueName(string baseName)
    {
        var name = string.IsNullOrWhiteSpace(baseName) ? "自定义主题" : baseName.Trim();

        if (!Themes.Any(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            return name;
        }

        for (var index = 2; index < 1000; index++)
        {
            var candidate = $"{name} ({index})";
            if (!Themes.Any(t => string.Equals(t.Name, candidate, StringComparison.OrdinalIgnoreCase)))
            {
                return candidate;
            }
        }

        return $"{name} ({Guid.NewGuid():N})";
    }
}
