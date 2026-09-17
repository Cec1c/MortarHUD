using System.IO;
using System.Threading;
using System.Windows;
using MortarHUD.App.Services;
using MortarHUD.App.Tray;
using MortarHUD.App.Views;
using MortarHUD.Capture;
using MortarHUD.Capture.Diagnostics;
using MortarHUD.Capture.ImageProcessing;
using MortarHUD.Capture.Ocr;
using MortarHUD.Capture.ScreenCapture;
using MortarHUD.Core.Ballistics;
using MortarHUD.Core.Configuration;
using MortarHUD.Core.Diagnostics;
using MortarHUD.Core.Models;
using MortarHUD.Core.Parsing;
using MortarHUD.Core.Session;
using MortarHUD.Core.Validation;
using MortarHUD.Platform.Windows.Dpi;
using MortarHUD.Platform.Windows.Hotkeys;
using MortarHUD.Platform.Windows.Mouse;
using MortarHUD.Platform.Windows.Startup;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace MortarHUD.App;

/// <summary>
/// 组装根 + 生命周期管理。
/// </summary>
/// <remarks>
/// 刻意不引入 DI 容器：这里要装配的东西不到二十个，而且
/// OCR 引擎、采集服务都需要按设置变化重建，手写装配反而更清楚。
/// </remarks>
public partial class App : Application
{
    private const string SingleInstanceMutexName = @"Global\MortarHUD.SingleInstance";

    private Mutex? _singleInstanceMutex;
    private FileLoggerProvider? _logProvider;
    private ILoggerFactory? _loggerFactory;
    private ILogger<App>? _logger;

    private SettingsStore? _settingsStore;
    private MortarHudSettings _settings = new();

    private GlobalHotkeyManager? _hotkeys;
    private TrayIcon? _tray;
    private OverlayWindow? _overlay;
    private DebugOverlayWindow? _debugOverlay;
    private SettingsWindow? _settingsWindow;

    private MortarSession? _session;
    private HudController? _hud;
    private MortarCaptureService? _captureService;
    private DebugArtifactWriter? _debugWriter;
    private CaptureDiagnostics? _debugObserver;
    private CoordinateRecognizer? _recognizer;

    private readonly LatestOperationRunner _operations = new();
    private List<ICoordinateOcrEngine> _ocrEngines = [];

    /// <summary>OnStartup 是否已完整跑完。用于区分「启动期崩溃」和「运行期崩溃」。</summary>
    private bool _startupCompleted;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 全局异常兜底。
        //
        // 之前这里没有处理，结果是：启动阶段抛一个 NullReferenceException，
        // 程序在没显示任何窗口的情况下直接消失，日志里一个字都没有——
        // 用户只看到进程闪一下就没了，完全没有可排查的线索。
        // 对这类后台工具来说，「静默死掉」是最糟的失败方式。
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        // 清单里已经声明了 PerMonitorV2，这里再调一次是兜底（比如从 IDE 直接运行时）。
        DpiAwareness.EnablePerMonitorV2();

        AppPaths.EnsureDirectories();

        // ---- 诊断模式 ----
        //
        // 自检与离屏截图要放在单实例检查<em>之前</em>：
        // 它们都是无人值守跑的（脚本、CI），一旦被「已在运行」的模态框拦住就会永远卡住。
        // 这两种模式既不显示窗口也不注册热键，和正在运行的实例不冲突。

        if (e.Args.Contains("--selftest", StringComparer.OrdinalIgnoreCase))
        {
            RunSelfTest();
            return;
        }

        var shotIndex = Array.IndexOf(e.Args, "--screenshot");
        if (shotIndex >= 0)
        {
            RunScreenshot(e.Args, shotIndex + 1 < e.Args.Length ? e.Args[shotIndex + 1] : ".");
            return;
        }

        // ---- 正常运行 ----

        // 单实例：跑两份会出现两个 Overlay 抢置顶，热键第二次注册也必然失败。
        _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show(
                "MortarHUD 已经在运行了，请从系统托盘打开设置。",
                "MortarHUD",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            Shutdown();
            return;
        }

        _settingsStore = new SettingsStore();
        _settings = _settingsStore.Load();

        SetupLogging();
        _logger?.LogInformation("MortarHUD 启动（schema v{Version}）", _settings.SchemaVersion);

        BuildCoreServices();
        BuildShell();

        RegisterHotkeys();

        _hud!.ApplySettings(_settings.Hud);

        ApplyStartupRegistration();

        if (!_settings.General.StartMinimized)
        {
            ShowSettings();
        }

        _startupCompleted = true;
        _logger?.LogInformation("启动完成");
    }

    /// <summary>
    /// 启动自检：构造全部窗口与视图模型，验证 XAML 与初始化顺序没问题。
    /// </summary>
    /// <remarks>
    /// 只构造不显示，也不注册热键，因此可以在用户正在游戏时安全运行。
    /// 退出码 0 表示通过。
    /// </remarks>
    private void RunSelfTest()
    {
        var failures = new List<string>();

        void Step(string name, Action action)
        {
            try
            {
                action();
                Console.WriteLine($"[ OK ] {name}");
            }
            catch (Exception ex)
            {
                failures.Add($"{name}: {ex.GetType().Name}: {ex.Message}");
                Console.WriteLine($"[FAIL] {name}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        // WinExe 默认不继承父进程的控制台，Console.WriteLine 会石沉大海。
        // 挂上去之后从命令行跑自检就能直接看到结果。
        MortarHUD.Platform.Windows.NativeMethods.Win32.AttachConsole(
            MortarHUD.Platform.Windows.NativeMethods.Win32.ATTACH_PARENT_PROCESS);

        Console.WriteLine();
        Console.WriteLine("MortarHUD 启动自检");
        Console.WriteLine(new string('-', 60));

        SetupLogging();

        Step("使用默认设置", () => _settings = new MortarHudSettings());
        Step("构建 OCR / 采集链路", BuildCoreServices);
        Step("构造托盘图标（不显示）", () => _tray = new TrayIcon(visible: false));
        Step("构造设置视图模型", () => _ = new ViewModels.SettingsViewModel(_settings));

        // 这一条就是之前启动即崩的地方：SettingsWindow 的构造函数。
        Step("构造设置窗口（含全部 3 个页面与 HUD 预览）", () =>
        {
            _settingsWindow = new SettingsWindow(_settings, () => Task.CompletedTask, TestCaptureAsync);
        });

        Step("构造 HUD Overlay 窗口", () => _overlay = new OverlayWindow());
        Step("构造 Debug 面板窗口", () => _debugOverlay = new DebugOverlayWindow());
        Step("构造 HUD 控制器", () => _hud = new HudController(_session!, _overlay!, () => _settings.Hud));

        Step("跑一次端到端识别", () =>
        {
            using var sample = Cv2.ImRead(Path.Combine(AppContext.BaseDirectory, "Models", "selftest", "roi.png"));
            var result = _recognizer!.RecognizeAsync(sample, CancellationToken.None).GetAwaiter().GetResult();
            if (!result.Success || result.Coordinate is not { } c
                || Math.Abs(c.X - 107.66) > .005 || Math.Abs(c.Y - 114.54) > .005)
                throw new InvalidOperationException($"实机样本识别失败：{result.Error} {result.Coordinate}");
            Console.WriteLine($"       实机样本：{result.Coordinate}，无需截取桌面");
        });

        Console.WriteLine(new string('-', 60));

        if (failures.Count == 0)
        {
            Console.WriteLine("自检通过。");
            _logger?.LogInformation("启动自检通过");
        }
        else
        {
            Console.WriteLine($"自检失败 {failures.Count} 项：");
            foreach (var failure in failures)
            {
                Console.WriteLine($"  - {failure}");
            }

            _logger?.LogError("启动自检失败：{Failures}", string.Join(" | ", failures));
        }

        Shutdown(failures.Count == 0 ? 0 : 1);
    }

    /// <summary>
    /// 把设置窗口的每个页面离屏渲染成 PNG。
    /// </summary>
    /// <remarks>
    /// 走 <see cref="System.Windows.Media.Imaging.RenderTargetBitmap"/>，
    /// 只需要 Measure/Arrange 就能出图，窗口全程不显示、不抢焦点。
    /// </remarks>
    private void RunScreenshot(string[] args, string outputDirectory)
    {
        MortarHUD.Platform.Windows.NativeMethods.Win32.AttachConsole(
            MortarHUD.Platform.Windows.NativeMethods.Win32.ATTACH_PARENT_PROCESS);

        try
        {
            SetupLogging();
            _settings = new SettingsStore().Load();

            var small = args.Contains("--compact");
            var width = small ? 920 : 1040;
            var height = small ? 680 : 760;

            var window = new SettingsWindow(_settings, () => Task.CompletedTask, TestCaptureAsync);
            window.Width = width;
            window.Height = height;
            if (args.Contains("--expanded"))
                foreach (var expander in LogicalDescendants(window).OfType<System.Windows.Controls.Expander>())
                    expander.IsExpanded = true;

            if (window.Content is not System.Windows.Controls.Panel root)
            {
                Console.WriteLine("设置窗口的根元素不是 Panel，无法离屏渲染。");
                Shutdown(1);
                return;
            }

            // 窗口自身的背景不在 Content 里，补上，否则出图是透明的。
            root.Background = window.Background;

            var tabs = window.FindName("Tabs") as System.Windows.Controls.TabControl;
            var count = tabs?.Items.Count ?? 1;

            Directory.CreateDirectory(outputDirectory);

            for (var index = 0; index < count; index++)
            {
                if (tabs is not null)
                {
                    tabs.SelectedIndex = index;
                }

                root.Measure(new System.Windows.Size(width, height));
                root.Arrange(new System.Windows.Rect(0, 0, width, height));
                root.UpdateLayout();

                // 布局里有 Dispatcher 排队的工作（比如预览重绘），先让它跑完。
                Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);

                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
                    width, height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                bitmap.Render(root);

                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));

                var tabName = tabs?.Items[index] is System.Windows.Controls.TabItem item
                    ? item.Header?.ToString() ?? index.ToString()
                    : "page";

                var path = Path.Combine(outputDirectory, $"{index + 1:00}-{tabName}.png");

                using (var stream = File.Create(path))
                {
                    encoder.Save(stream);
                }

                Console.WriteLine($"已渲染 {path}");
            }

            Console.WriteLine($"共 {count} 页。");
            Shutdown(0);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"离屏渲染失败：{ex}");
            Shutdown(1);
        }
    }

    // ------------------------------------------------------------ 全局异常

    private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        // UI 线程上的异常。标记 Handled 让程序活下来：
        // 对一个贴在游戏上的 HUD 来说，「某个设置项崩了」远好过「整个工具消失」。
        _logger?.LogError(e.Exception, "UI 线程未处理异常");

        // 初始化阶段出错就直接退出，半死不活的状态更难排查。
        if (!_startupCompleted)
        {
            ReportFatal("启动失败", e.Exception);
            e.Handled = true;
            Shutdown(1);
            return;
        }

        e.Handled = true;
        _tray?.ShowBalloon("MortarHUD 遇到错误", e.Exception.Message, isError: true);
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var exception = e.ExceptionObject as Exception;
        _logger?.LogCritical(exception, "非 UI 线程未处理异常，进程即将结束");

        if (e.IsTerminating)
        {
            ReportFatal("发生致命错误，程序即将退出", exception);
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _logger?.LogError(e.Exception, "未观察到的任务异常");
        e.SetObserved();
    }

    /// <summary>
    /// 把致命错误直白地告诉用户。
    /// </summary>
    /// <remarks>
    /// 之前这类错误是完全静默的：进程消失、没有日志、没有提示，
    /// 用户只能看到「程序运行一下就没了」。所以这里宁可弹一个框。
    /// </remarks>
    private static void ReportFatal(string title, Exception? exception)
    {
        try
        {
            var detail = exception is null
                ? "未知错误。详见 %AppData%\\MortarHUD\\Logs\\。"
                : $"{exception.GetType().Name}: {exception.Message}\n\n详见 %AppData%\\MortarHUD\\Logs\\。";

            MessageBox.Show(detail, $"MortarHUD — {title}", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"弹窗失败：{ex}");
        }
    }

    private static IEnumerable<DependencyObject> LogicalDescendants(DependencyObject parent)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var nested in LogicalDescendants(child)) yield return nested;
        }
    }

    private void SetupLogging()
    {
        _logProvider = new FileLoggerProvider();
        _loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddProvider(_logProvider);
        });

        _logger = _loggerFactory.CreateLogger<App>();
    }

    /// <summary>按当前设置重建「截图 → OCR → 解析 → 校验」这条链路。</summary>
    private void BuildCoreServices()
    {
        var calculator = new MortarCalculator(
            _settings.General.MetersPerCoordinateUnit,
            xPositiveIsEast: !string.Equals(_settings.General.XPositiveDirection, "West", StringComparison.OrdinalIgnoreCase),
            yPositiveIsNorth: !string.Equals(_settings.General.YPositiveDirection, "South", StringComparison.OrdinalIgnoreCase));

        _session = new MortarSession(calculator);

        var parser = new CoordinateTextParser(new CoordinateTextParserOptions
        {
            RequireDecimalPoint = _settings.Ocr.RequireDecimalPoint,
        });

        var validator = new CoordinateValidator(new CoordinateValidationOptions
        {
            CoordinateMin = _settings.Ocr.CoordinateMin,
            CoordinateMax = _settings.Ocr.CoordinateMax,
            MinimumConfidence = _settings.Ocr.MinimumConfidence,
        });

        var engines = OcrEngineFactory.Resolve(_settings.Ocr.Engine, _settings.Ocr, _settings.Ocr.TessdataPath).ToList();
        _ocrEngines = engines;
        var preprocessors = PreprocessorFactory.ResolveCandidates(_settings.Ocr.Preprocessor);

        _debugWriter = new DebugArtifactWriter();
        _debugObserver = new CaptureDiagnostics(
            _debugWriter, () => _settings.Debug, message => _logger?.LogWarning("{Message}", message));

        var recognizer = new CoordinateRecognizer(engines, preprocessors, parser, validator, _debugObserver);
        _recognizer = recognizer;

        _captureService = new MortarCaptureService(
            new GdiScreenCaptureProvider(),
            new WindowsCursorPositionProvider(),
            recognizer,
            () => _settings.Roi,
            message => _logger?.LogInformation("{Message}", message));

        _logger?.LogInformation(
            "OCR 链路就绪：引擎={Engines} 流水线={Pipelines}",
            string.Join("+", engines.Select(x => x.Name)),
            string.Join("+", preprocessors.Select(x => x.Name)));
    }

    private void BuildShell()
    {
        _overlay = new OverlayWindow();
        _overlay.PositionChangedByUser += (_, _) => PersistSettings();

        // TDD §34：Debug 面板是独立窗口，和主 HUD 分开。
        _debugOverlay = new DebugOverlayWindow();

        _hud = new HudController(_session!, _overlay, () => _settings.Hud);

        _tray = new TrayIcon();
        _tray.ShowSettingsRequested += (_, _) => ShowSettings();
        _tray.ToggleHudRequested += (_, _) => ToggleHud();
        _tray.ToggleDebugRequested += (_, _) => ToggleDebug();
        _tray.CaptureGunRequested += (_, _) => _ = CaptureAsync(isGun: true);
        _tray.CaptureTargetRequested += (_, _) => _ = CaptureAsync(isGun: false);
        _tray.ExitRequested += (_, _) => ExitApplication();
    }

    // ------------------------------------------------------------ 热键

    /// <summary>
    /// 重新注册全部热键。
    /// </summary>
    /// <remarks>
    /// <strong>必须复用同一个 <see cref="GlobalHotkeyManager"/> 实例。</strong>
    /// 之前这里是「每次调用都 new 一个」，而旧的从来没被 Dispose——
    /// 旧实例的 message-only 窗口依然活着、依然占着 F6~F9，
    /// 于是新实例注册必然失败，用户看到的就是「热键一直提示注册失败」。
    /// 自锁，而且看起来像被别的软件占用，非常难判断。
    /// </remarks>
    private void RegisterHotkeys()
    {
        _hotkeys ??= CreateHotkeyManager();

        var bindings = new Dictionary<HotkeyAction, HotkeyDefinition>();
        var errors = new List<string>();

        foreach (var (action, text) in EnumerateHotkeyBindings())
        {
            if (HotkeyParser.TryParse(text, out var definition, out var parseError))
            {
                bindings[action] = definition;
            }
            else
            {
                errors.Add($"{GlobalHotkeyManager.Describe(action)}（{text}）：{parseError}");
            }
        }

        foreach (var result in _hotkeys.Apply(bindings))
        {
            if (result.Success)
            {
                _logger?.LogInformation("热键已注册：{Action} = {Hotkey}", result.Action, result.Definition);
            }
            else
            {
                // TDD §18 / §38：注册失败必须明确提示，不能静默失效。
                errors.Add(result.Error ?? $"{result.Action} 注册失败。");
            }
        }

        // 观察型热键：只监听不拦截。地图键必须照常送到游戏手里，否则地图打不开。
        if (_settings.Hotkeys.AutoCalibrateEnabled)
        {
            if (HotkeyParser.TryParse(_settings.Hotkeys.AutoCalibrateKey, out var mapKey, out var mapKeyError))
            {
                var observed = _hotkeys.ApplyObserved(
                    new Dictionary<HotkeyAction, HotkeyDefinition> { [HotkeyAction.AutoCalibrateGun] = mapKey });

                foreach (var result in observed.Where(r => !r.Success))
                {
                    errors.Add(result.Error ?? "地图键监听失败。");
                }

                _logger?.LogInformation("地图键监听已启用：{Key}（延迟 {Delay}ms）",
                    mapKey.ToDisplayString(), _settings.Hotkeys.AutoCalibrateDelayMs);
            }
            else
            {
                errors.Add($"地图键「{_settings.Hotkeys.AutoCalibrateKey}」无法识别：{mapKeyError}");
            }
        }

        _tray?.UpdateHotkeys(
            DescribeHotkey(_settings.Hotkeys.CaptureGun),
            DescribeHotkey(_settings.Hotkeys.CaptureTarget));

        if (errors.Count > 0)
        {
            _tray?.ShowBalloon(
                "热键注册失败",
                string.Join(Environment.NewLine, errors),
                isError: true);
        }
    }

    private static string DescribeHotkey(string? config)
        => HotkeyParser.TryParse(config, out var definition, out _) ? definition.ToDisplayString() : "";

    private GlobalHotkeyManager CreateHotkeyManager()
    {
        var manager = new GlobalHotkeyManager();

        // 输入监听失效时是「什么都不发生」，没有日志就无从判断。
        manager.Log = message => _logger?.LogInformation("[输入] {Message}", message);
        manager.HotkeyPressed += OnHotkeyPressed;
        manager.UserActivity += (_, _) => _operations.CancelOnActivity();
        return manager;
    }

    private IEnumerable<(HotkeyAction Action, string Text)> EnumerateHotkeyBindings()
    {
        yield return (HotkeyAction.CaptureGun, _settings.Hotkeys.CaptureGun);
        yield return (HotkeyAction.CaptureTarget, _settings.Hotkeys.CaptureTarget);
        yield return (HotkeyAction.ToggleHud, _settings.Hotkeys.ToggleHud);
        yield return (HotkeyAction.OpenSettings, _settings.Hotkeys.OpenSettings);
    }

    private void OnHotkeyPressed(object? sender, HotkeyPressedEventArgs e)
    {
        // 热键来自 GlobalHotkeyManager 自己的消息线程，必须切回 UI 线程。
        Dispatcher.BeginInvoke(() =>
        {
            _logger?.LogInformation("热键触发：{Action}", e.Action);

            switch (e.Action)
            {
                case HotkeyAction.CaptureGun:
                    _ = CaptureAsync(isGun: true);
                    break;
                case HotkeyAction.CaptureTarget:
                    _ = CaptureAsync(isGun: false);
                    break;
                case HotkeyAction.ToggleHud:
                    ToggleHud();
                    break;
                case HotkeyAction.OpenSettings:
                    ShowSettings();
                    break;
                case HotkeyAction.AutoCalibrateGun:
                    _ = AutoCalibrateGunAsync();
                    break;
            }
        });
    }

    // ------------------------------------------------------------ 采集

    private Task CaptureAsync(bool isGun) => RunCaptureAsync(isGun, automatic: false);

    private Task AutoCalibrateGunAsync() => RunCaptureAsync(isGun: true, automatic: true);

    private async Task RunCaptureAsync(bool isGun, bool automatic)
    {
        var foreground = CaptureContext.Foreground;
        if (automatic && !CaptureContext.IsExternal(foreground)) return;
        CaptureContext.TryGetCursor(out var initialCursor);
        await _operations.RunAsync(async token =>
        {
            try
            {
                if (_captureService is null || _session is null || _hud is null) return;
                if (!isGun && _session.Gun is null)
                {
                    _operations.TryCommit(token, () =>
                    {
                        _session.ReportFailure(MortarStatusKind.NoGunPosition, "NO_GUN_POSITION");
                        _hud.NotifyStatusChanged();
                    });
                    return;
                }
                if (automatic)
                {
                    await Task.Delay(Math.Clamp(_settings.Hotkeys.AutoCalibrateDelayMs, 0, 3000), token);
                    if (!CaptureContext.TryGetCursor(out initialCursor)
                        || !CaptureContext.IsClientCenter(foreground, initialCursor))
                    {
                        _logger?.LogInformation("自动校准跳过：光标尚未归位到前台窗口中心，请手动记录炮位");
                        return;
                    }
                }
                var attempts = automatic ? 2 : 3;
                for (var attempt = 0; attempt < attempts; attempt++)
                {
                    if (attempt > 0) await Task.Delay(automatic ? 150 : 120, token);
                    token.ThrowIfCancellationRequested();
                    if (!ContextUnchanged()) return;
                    _debugObserver?.BeginRequest();
                    var outcome = await _captureService.CaptureAsync(token);
                    token.ThrowIfCancellationRequested();
                    if (!ContextUnchanged()) return;
                    if (outcome.Recognition.Error == "BUSY") return;
                    outcome = _debugObserver?.AttachImages(outcome) ?? outcome;
                    _debugObserver?.WriteResult(outcome, isGun);
                    _logger?.LogInformation("采集 {Kind} 第 {Attempt} 次：{Details}",
                        isGun ? "炮位" : "目标", attempt + 1,
                        CoordinateRecognizer.DescribeAttempts(outcome.Recognition.Attempts));
                    if (outcome.Success || attempt == attempts - 1)
                    {
                        _operations.TryCommit(token, () => ApplyCapture(outcome, isGun));
                        return;
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "采集失败");
                _operations.TryCommit(token, () =>
                {
                    _session?.ReportFailure(MortarStatusKind.CaptureFailed, ex.Message);
                    _hud?.NotifyStatusChanged();
                });
            }
        });

        bool ContextUnchanged() => CaptureContext.Foreground == foreground
            && CaptureContext.TryGetCursor(out var cursor) && cursor == initialCursor;
    }

    private void ApplyCapture(CaptureOutcome outcome, bool isGun)
    {
        if (_session is null || _hud is null) return;
        if (outcome.Success && outcome.Coordinate is { } coordinate)
        {
            if (isGun) _session.LockGun(coordinate);
            else _session.LockTarget(coordinate);
            _logger?.LogInformation("{Kind}已锁定：{X:0.00} / {Y:0.00}",
                isGun ? "炮位" : "目标", coordinate.X, coordinate.Y);
        }
        else
        {
            _session.ReportFailure(MapFailureStatus(outcome.Recognition.Error), outcome.Recognition.Error);
            _logger?.LogWarning("采集失败：{Error}；光标={Cursor} ROI={Roi}",
                outcome.Recognition.Error, outcome.Cursor, outcome.Roi);
        }
        _hud.NotifyStatusChanged();
        UpdateDebugOverlay(outcome);
    }

    /// <summary>
    /// 设置页「Test OCR」用：只做一次采集识别，不碰炮位 / 目标。
    /// </summary>
    /// <remarks>
    /// 和热键那条路径分开，是因为测试不该有副作用——
    /// 用户只是想看看 ROI 框没框对，不该因此把已经锁好的目标覆盖掉。
    /// </remarks>
    private async Task<CaptureOutcome> TestCaptureAsync()
    {
        var result = CaptureOutcome.Failed("CANCELLED", TimeSpan.Zero);
        await _operations.RunAsync(async token =>
        {
            if (_captureService is null) return;
            _debugObserver?.BeginRequest();
            var captured = await _captureService.CaptureAsync(token);
            token.ThrowIfCancellationRequested();
            _operations.TryCommit(token, () =>
            {
                result = _debugObserver?.AttachImages(captured) ?? captured;
                _debugObserver?.WriteResult(result, false);
            });
        });
        return result;
    }

    /// <summary>
    /// 把最近一次采集的结果推给 Debug 面板（TDD §34）。
    /// </summary>
    /// <remarks>
    /// 即使面板当前不可见也照样更新——这样用户刚打开 Debug 开关时，
    /// 看到的是上一次采集的真实数据，而不是一片空白。
    /// </remarks>
    private void UpdateDebugOverlay(CaptureOutcome outcome)
    {
        if (_debugOverlay is null)
        {
            return;
        }

        var attempts = outcome.Recognition.Attempts;

        var snapshot = new DebugSnapshot
        {
            CursorX = outcome.Cursor.X,
            CursorY = outcome.Cursor.Y,
            Roi = outcome.Roi,
            Success = outcome.Success,
            Coordinate = outcome.Coordinate,
            Confidence = outcome.Recognition.Confidence,
            RawText = outcome.Recognition.RawText,
            Error = outcome.Recognition.Error,
            OcrMilliseconds = attempts.Sum(a => a.OcrTime.TotalMilliseconds),
            TotalMilliseconds = outcome.TotalTime.TotalMilliseconds,
            PipelineSummary = attempts.Select(a => a.ToString()).ToList(),
        };

        _debugOverlay.ApplySettings(_settings.Hud, _settings.Debug);
        _debugOverlay.UpdateContent(snapshot, _settings.Debug);
    }

    /// <summary>把设置里的「开机自动启动」落到注册表（TDD §28）。</summary>
    private void ApplyStartupRegistration()
    {
        var desired = _settings.General.StartWithWindows;

        if (WindowsStartupRegistration.IsEnabled() == desired)
        {
            return;
        }

        if (WindowsStartupRegistration.SetEnabled(desired, out var error))
        {
            _logger?.LogInformation("开机自启已{State}", desired ? "启用" : "关闭");
        }
        else
        {
            _logger?.LogWarning("设置开机自启失败：{Error}", error);
            _tray?.ShowBalloon("开机自启设置失败", error ?? "未知原因", isError: true);
        }
    }

    /// <summary>把识别层的错误串映射成要显示给用户的短状态（TDD §38）。</summary>
    private static MortarStatusKind MapFailureStatus(string? error) => error switch
    {
        null => MortarStatusKind.OcrFailed,
        var e when e.StartsWith("CAPTURE_FAILED", StringComparison.Ordinal) => MortarStatusKind.CaptureFailed,
        var e when e.Contains("OUT_OF_RANGE", StringComparison.Ordinal)
                   || e.Contains("NON_FINITE", StringComparison.Ordinal) => MortarStatusKind.InvalidCoordinate,
        _ => MortarStatusKind.OcrFailed,
    };

    // ------------------------------------------------------------ 界面动作

    private void ShowSettings()
    {
        _operations.Cancel();
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(_settings, ApplySettingsFromUi, TestCaptureAsync, count => _debugObserver?.CollectNext(count));
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }

        _settingsWindow.Show();

        // 设置窗口必须能抢到焦点，否则用户没法在里面打字。
        if (_settingsWindow.WindowState == WindowState.Minimized)
        {
            _settingsWindow.WindowState = WindowState.Normal;
        }

        _settingsWindow.Activate();
    }

    private void ToggleHud()
    {
        _operations.Cancel();
        _settings.Hud.Visible = !_settings.Hud.Visible;
        _hud?.ApplySettings(_settings.Hud);
        _debugOverlay?.ApplySettings(_settings.Hud, _settings.Debug);

        _tray?.UpdateState(_settings.Hud.Visible, _settings.Debug.Enabled, _settings.Hud.PositionUnlocked);
        PersistSettings();
    }

    private void ToggleDebug()
    {
        _operations.Cancel();
        _settings.Debug.Enabled = !_settings.Debug.Enabled;

        if (!_settings.Debug.Enabled)
        {
            _debugOverlay?.ApplySettings(_settings.Hud, _settings.Debug);
        }

        if (_settings.Debug.Enabled)
        {
            // 打开 Debug 时把常用的几项一并打开，否则用户会以为没生效。
            _settings.Debug.ShowRawOcrText = true;
            _settings.Debug.ShowParsedCoordinates = true;
            _settings.Debug.ShowConfidence = true;
            _settings.Debug.ShowTiming = true;
        }

        _hud?.Refresh();
        _debugOverlay?.ApplySettings(_settings.Hud, _settings.Debug);
        _tray?.UpdateState(_settings.Hud.Visible, _settings.Debug.Enabled, _settings.Hud.PositionUnlocked);
        PersistSettings();
    }

    /// <summary>设置页点了「应用」之后调用：重建受影响的链路并刷新 HUD。</summary>
    private Task ApplySettingsFromUi() => _operations.RunAsync(token =>
    {
        try
        {
            // OCR / 坐标范围 / 单位换算都可能改了，整条链路重建最稳妥。
            var gun = _session?.Gun;
            var target = _session?.Target;
            DisposeCoreServices();
            BuildCoreServices();
            if (gun is { } g) _session!.LockGun(g);
            if (target is { } t) _session!.LockTarget(t);

            // HudController 持有 session 引用。BuildCoreServices 换的是新 session，
            // 不重建它的话，HUD 会一直显示旧 session 的数据——
            // 表现就是「点完应用，程序还在但什么都不更新了」。
            _hud?.Dispose();
            _hud = new HudController(_session!, _overlay!, () => _settings.Hud);

            _hud.ApplySettings(_settings.Hud);
            _debugOverlay?.ApplySettings(_settings.Hud, _settings.Debug);
            _tray?.UpdateState(_settings.Hud.Visible, _settings.Debug.Enabled, _settings.Hud.PositionUnlocked);

            ApplyStartupRegistration();
            RegisterHotkeys();
            PersistSettings();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "应用设置失败");
            throw;
        }
        return Task.CompletedTask;
    }, cancelOnActivity: false, throwOnCancellation: true);

    private void PersistSettings()
    {
        try
        {
            _settingsStore?.Save(_settings);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "保存设置失败");
        }
    }

    // ------------------------------------------------------------ 退出

    private async void ExitApplication()
    {
        await _operations.RunAsync(_ => { DisposeCoreServices(); return Task.CompletedTask; });
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _operations.Cancel();
        // 退出前取消尚未提交的操作。
        try
        {
            PersistSettings();
            _logger?.LogInformation("MortarHUD 退出");

            _hotkeys?.Dispose();

            _hud?.Dispose();
            _settingsWindow?.Close();
            _overlay?.Close();
            _debugOverlay?.Close();

            DisposeCoreServices();

            _tray?.Dispose();

            _loggerFactory?.Dispose();
            _logProvider?.Dispose();

            _singleInstanceMutex?.ReleaseMutex();
            _singleInstanceMutex?.Dispose();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"退出清理时出错：{ex}");
        }

        base.OnExit(e);
    }

    private void DisposeCoreServices()
    {
        _captureService?.Dispose();
        _captureService = null;

        // OCR 引擎持有 Tesseract / leptonica 的原生句柄，必须显式释放。
        // 漏掉它们的话，退出时 Tesseract 的 ObjectCache 会刷一屏 LEAK 警告，
        // 而且每次在设置页点「应用」重建链路都会再漏一份。
        foreach (var engine in _ocrEngines)
        {
            engine.Dispose();
        }

        _ocrEngines = [];
        _debugObserver = null;
    }

}
