using System.Collections.Concurrent;
using MortarHUD.Localization;
using System.Runtime.InteropServices;
using MortarHUD.Platform.Windows.NativeMethods;

namespace MortarHUD.Platform.Windows.Hotkeys;

/// <summary>
/// 全局热键服务，支持三类绑定。
/// </summary>
/// <remarks>
/// <para>
/// <strong>键盘键</strong>（F6、Ctrl+G……）走 <c>RegisterHotKey</c>：
/// 系统级注册，零开销，且本来就是「拦截式」的——这正是我们想要的。
/// </para>
/// <para>
/// <strong>鼠标键</strong>与<strong>观察型键盘键</strong>（例如「按 M 打开地图」）
/// 走 <c>RegisterRawInputDevices</c> + <c>RIDEV_INPUTSINK</c>：
/// 内核把事件直接投递到窗口消息队列，<em>不拦截</em>，被观察的键照常送到游戏手里。
/// </para>
/// <para>
/// 为什么不用低级钩子（WH_MOUSE_LL / WH_KEYBOARD_LL）：
/// Windows 对钩子回调有硬性超时（默认 300ms），一旦某次回调超时，
/// 系统会<strong>不报错、不通知</strong>地把钩子摘掉，而且永不恢复。
/// 用户看到的就是「刚启动时好使，用着用着彻底没反应」——
/// 这个坑踩过两次，加固回调只是治标。原始输入根本没有回调，也就没有超时这回事。
/// </para>
/// <para>
/// 自建一条消息线程承载窗口：<c>RegisterHotKey</c> 的 WM_HOTKEY 会投递到
/// 创建窗口的那条线程，原始输入的 WM_INPUT 同理，两者落在同一条线程上最简单。
/// </para>
/// </remarks>
public sealed class GlobalHotkeyManager : IGlobalHotkeyService
{
    private const uint WmExecuteQueue = Win32.WM_APP + 1;
    private const uint WmRaiseHotkey = Win32.WM_APP + 2;

    private readonly ConcurrentQueue<Action> _workQueue = new();
    private readonly ConcurrentQueue<HotkeyAction> _pendingHotkeys = new();
    private readonly Dictionary<int, HotkeyAction> _idToAction = [];
    private readonly Dictionary<HotkeyAction, HotkeyDefinition> _actionToDefinition = [];

    /// <summary>
    /// 原始输入要查的两张表。
    /// </summary>
    /// <remarks>
    /// 不可变快照 + 整体替换，读方不加锁。原始输入虽然没有钩子那种超时限制，
    /// 但消息线程上少一处加锁就少一个被别的线程卡住的可能。
    /// </remarks>
    private volatile Dictionary<(MouseButton Button, uint Modifiers), HotkeyAction> _mouseBindings = [];

    private volatile Dictionary<uint, HotkeyAction> _observedKeys = [];
    private readonly ObservedKeyState _keyState = new();

    /// <summary>必须保持强引用，否则委托被 GC 后窗口过程会指向野指针。</summary>
    private readonly Win32.WndProcDelegate _wndProcDelegate;

    private readonly ManualResetEventSlim _ready = new(false);
    private readonly object _sync = new();

    private Thread? _messageThread;
    private IntPtr _hwnd;
    private ushort _classAtom;
    private string? _className;
    private int _nextHotkeyId = 1;
    private bool _disposed;

    public GlobalHotkeyManager()
    {
        _wndProcDelegate = WindowProc;
        StartMessageThread();
    }

    public event EventHandler<HotkeyPressedEventArgs>? HotkeyPressed;
    public event EventHandler? UserActivity;

    /// <summary>
    /// 诊断日志出口，App 会把它接到文件日志上。
    /// </summary>
    /// <remarks>
    /// 输入监听一旦失效就是「什么都不发生」，没有日志根本无从判断。
    /// </remarks>
    public Action<string>? Log { get; set; }

    // ================================================================ 注册

    public IReadOnlyList<HotkeyRegistrationResult> Apply(
        IReadOnlyDictionary<HotkeyAction, HotkeyDefinition> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var results = new List<HotkeyRegistrationResult>(bindings.Count);

        RunOnMessageThread(() =>
        {
            lock (_sync)
            {
                UnregisterAllCore();

                // 先做进程内查重，这样两个功能绑同一个键时能给出比 Win32 错误码更有用的提示。
                var seen = new Dictionary<HotkeyDefinition, HotkeyAction>();

                foreach (var (action, definition) in bindings)
                {
                    if (!definition.IsValid)
                    {
                        results.Add(HotkeyRegistrationResult.Failed(action, definition, Loc.T("TheHotkeyIsNotSet")));
                        continue;
                    }

                    if (seen.TryGetValue(definition, out var owner))
                    {
                        results.Add(HotkeyRegistrationResult.Failed(
                            action,
                            definition,
                            $"「{definition.ToDisplayString()}」已经绑定给 {Describe(owner)}，两个功能不能共用同一个热键。"));
                        continue;
                    }

                    seen[definition] = action;

                    results.Add(definition.IsMouse
                        ? RegisterMouse(action, definition)
                        : RegisterKeyboard(action, definition));
                }

                EnsureRawInput();
            }
        });

        return results;
    }

    public IReadOnlyList<HotkeyRegistrationResult> ApplyObserved(
        IReadOnlyDictionary<HotkeyAction, HotkeyDefinition> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var results = new List<HotkeyRegistrationResult>(bindings.Count);

        RunOnMessageThread(() =>
        {
            lock (_sync)
            {
                _observedKeys = [];
                _keyState.Clear();

                foreach (var (action, definition) in bindings)
                {
                    // 观察型只能绑单个裸键：带修饰键的话，游戏那边同样会收到组合键，
                    // 语义容易混乱，而且原始输入里判断修饰键的状态本身也有时序问题。
                    if (!definition.IsValid || definition.IsMouse || definition.Modifiers != 0)
                    {
                        results.Add(HotkeyRegistrationResult.Failed(
                            action, definition, Loc.T("ThisCanOnlyBeBoundToASingleKeyboardKeyWithNoModi")));
                        continue;
                    }

                    _observedKeys = new Dictionary<uint, HotkeyAction>(_observedKeys)
                    {
                        [definition.VirtualKey] = action,
                    };

                    _actionToDefinition[action] = definition;
                    results.Add(HotkeyRegistrationResult.Ok(action, definition));
                }

                EnsureRawInput();
            }
        });

        return results;
    }

    private HotkeyRegistrationResult RegisterKeyboard(HotkeyAction action, HotkeyDefinition definition)
    {
        var id = _nextHotkeyId++;

        if (!Win32.RegisterHotKey(_hwnd, id, definition.RegistrationModifiers, definition.VirtualKey))
        {
            var code = Marshal.GetLastWin32Error();

            return HotkeyRegistrationResult.Failed(
                action,
                definition,
                code == 1409
                    ? $"「{definition.ToDisplayString()}」注册失败：该组合键已被其它程序占用。"
                    : $"「{definition.ToDisplayString()}」注册失败（Win32 错误 {code}）。");
        }

        _idToAction[id] = action;
        _actionToDefinition[action] = definition;
        return HotkeyRegistrationResult.Ok(action, definition);
    }

    /// <summary>
    /// 登记一个鼠标热键。
    /// </summary>
    /// <remarks>
    /// 鼠标键没有「被占用」这一说——原始输入是观察式的，任何键都能绑。
    /// </remarks>
    private HotkeyRegistrationResult RegisterMouse(HotkeyAction action, HotkeyDefinition definition)
    {
        if (_hwnd == IntPtr.Zero)
        {
            return HotkeyRegistrationResult.Failed(action, definition, Loc.T("TheHotkeyMessageWindowIsNotReady"));
        }

        _mouseBindings = new Dictionary<(MouseButton, uint), HotkeyAction>(_mouseBindings)
        {
            [((MouseButton)definition.VirtualKey, definition.Modifiers)] = action,
        };

        _actionToDefinition[action] = definition;
        return HotkeyRegistrationResult.Ok(action, definition);
    }

    /// <summary>注册原始输入设备。重复调用无害。</summary>
    private void EnsureRawInput()
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        var devices = new[]
        {
            new RawInput.RAWINPUTDEVICE
            {
                usUsagePage = RawInput.HID_USAGE_PAGE_GENERIC,
                usUsage = RawInput.HID_USAGE_GENERIC_MOUSE,
                dwFlags = RawInput.RIDEV_INPUTSINK,
                hwndTarget = _hwnd,
            },
            new RawInput.RAWINPUTDEVICE
            {
                usUsagePage = RawInput.HID_USAGE_PAGE_GENERIC,
                usUsage = RawInput.HID_USAGE_GENERIC_KEYBOARD,
                dwFlags = RawInput.RIDEV_INPUTSINK,
                hwndTarget = _hwnd,
            },
        };

        if (RawInput.RegisterRawInputDevices(
                devices,
                (uint)devices.Length,
                (uint)Marshal.SizeOf<RawInput.RAWINPUTDEVICE>()))
        {
            Log?.Invoke("原始输入已注册（鼠标 + 键盘，INPUTSINK）");
            return;
        }

        Log?.Invoke($"原始输入注册失败（Win32 错误 {Marshal.GetLastWin32Error()}），"
                    + Loc.T("MouseHotkeysAndTheMapKeyWillBeUnavailable"));
    }

    public void UnregisterAll()
    {
        if (_disposed)
        {
            return;
        }

        RunOnMessageThread(() =>
        {
            lock (_sync)
            {
                UnregisterAllCore();
            }
        });
    }

    private void UnregisterAllCore()
    {
        if (_hwnd != IntPtr.Zero)
        {
            foreach (var id in _idToAction.Keys)
            {
                Win32.UnregisterHotKey(_hwnd, id);
            }
        }

        _idToAction.Clear();
        _mouseBindings = [];
        _observedKeys = [];
        _keyState.Clear();
        _actionToDefinition.Clear();
    }

    public static string Describe(HotkeyAction action) => action switch
    {
        HotkeyAction.CaptureGun => Loc.T("RecordGun"),
        HotkeyAction.CaptureTarget => Loc.T("RecordTarget"),
        HotkeyAction.ToggleHud => Loc.T("ToggleHUD"),
        HotkeyAction.OpenSettings => Loc.T("OpenSettings"),
        HotkeyAction.AutoCalibrateGun => Loc.T("AutoCalibrateTheGunFromTheMapKey"),
        _ => action.ToString(),
    };

    // ================================================================ 事件投递

    /// <summary>
    /// 把热键事件投回消息队列，等当前消息处理完再触发。
    /// </summary>
    /// <remarks>
    /// 订阅方（App）目前只做一次 Dispatcher.BeginInvoke，但那是「订阅方守规矩」的假设。
    /// 投递是微秒级的，不依赖任何假设。
    /// </remarks>
    private void RaiseHotkey(HotkeyAction action)
    {
        _pendingHotkeys.Enqueue(action);
        Win32.PostMessage(_hwnd, WmRaiseHotkey, IntPtr.Zero, IntPtr.Zero);
    }

    private void HandleRawInput(IntPtr lParam)
    {
        uint size = 0;
        var headerSize = (uint)Marshal.SizeOf<RawInput.RAWINPUTHEADER>();

        if (RawInput.GetRawInputData(lParam, RawInput.RID_INPUT, IntPtr.Zero, ref size, headerSize) != 0)
        {
            return;
        }

        if (size == 0 || size > 1024)
        {
            return;
        }

        var buffer = Marshal.AllocHGlobal((int)size);

        try
        {
            if (RawInput.GetRawInputData(lParam, RawInput.RID_INPUT, buffer, ref size, headerSize) == uint.MaxValue)
            {
                return;
            }

            var header = Marshal.PtrToStructure<RawInput.RAWINPUTHEADER>(buffer);

            if (header.dwType == RawInput.RIM_TYPEMOUSE)
            {
                HandleRawMouse(buffer);
            }
            else if (header.dwType == RawInput.RIM_TYPEKEYBOARD)
            {
                HandleRawKeyboard(buffer);
            }
        }
        catch (Exception ex)
        {
            Log?.Invoke($"处理原始输入失败：{ex.Message}");
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private void HandleRawMouse(IntPtr buffer)
    {
        var offset = Marshal.SizeOf<RawInput.RAWINPUTHEADER>();
        var flags = RawInput.ReadUInt16(buffer, offset + RawInput.MouseButtonFlagsOffset);

        // 只观察设备活动，不拦截输入；移动或滚轮使尚未提交的采集作废。
        if ((flags & 0x0D55) != 0 || Marshal.ReadInt32(buffer, offset + 12) != 0
            || Marshal.ReadInt32(buffer, offset + 16) != 0)
            UserActivity?.Invoke(this, EventArgs.Empty);

        var modifiers = ReadModifierState();
        // Flags 是位掩码：同时按键/滚轮不能让合法的按下事件消失。
        foreach (var (mask, button) in new[]
        {
            (RawInput.RI_MOUSE_LEFT_BUTTON_DOWN, MouseButton.Left),
            (RawInput.RI_MOUSE_RIGHT_BUTTON_DOWN, MouseButton.Right),
            (RawInput.RI_MOUSE_MIDDLE_BUTTON_DOWN, MouseButton.Middle),
            (RawInput.RI_MOUSE_BUTTON_4_DOWN, MouseButton.X1),
            (RawInput.RI_MOUSE_BUTTON_5_DOWN, MouseButton.X2),
        })
        {
            if ((flags & mask) != 0 && _mouseBindings.TryGetValue((button, modifiers), out var action))
                RaiseHotkey(action);
        }
    }

    private void HandleRawKeyboard(IntPtr buffer)
    {
        var offset = Marshal.SizeOf<RawInput.RAWINPUTHEADER>();
        var virtualKey = RawInput.ReadUInt16(buffer, offset + RawInput.KeyboardVKeyOffset);
        var flags = RawInput.ReadUInt16(buffer, offset + 2);
        var header = Marshal.PtrToStructure<RawInput.RAWINPUTHEADER>(buffer);
        if (!_keyState.Press(header.hDevice, virtualKey, flags)) return;

        // 同一个按下还会到 WM_HOTKEY，不能在两个通道里重复取消新发起的采集。
        var modifiers = ReadModifierState();
        var registered = _idToAction.Values.Any(a => _actionToDefinition.TryGetValue(a, out var definition)
            && definition.VirtualKey == virtualKey && definition.Modifiers == modifiers);
        if (!registered) UserActivity?.Invoke(this, EventArgs.Empty);

        if (modifiers == 0 && _observedKeys.TryGetValue(virtualKey, out var action))
        {
            RaiseHotkey(action);
        }
    }

    /// <summary>
    /// 读取当前按下的修饰键。
    /// </summary>
    /// <remarks>
    /// 用 <c>GetAsyncKeyState</c> 而不是 WPF 的 <c>Keyboard.Modifiers</c>：
    /// 这里在消息线程上，既不是 UI 线程，也不该依赖 UI 框架。
    /// </remarks>
    private static uint ReadModifierState()
    {
        uint modifiers = 0;

        if (IsDown(Win32.VK_CONTROL))
        {
            modifiers |= Win32.MOD_CONTROL;
        }

        if (IsDown(Win32.VK_MENU))
        {
            modifiers |= Win32.MOD_ALT;
        }

        if (IsDown(Win32.VK_SHIFT))
        {
            modifiers |= Win32.MOD_SHIFT;
        }

        if (IsDown(Win32.VK_LWIN) || IsDown(Win32.VK_RWIN))
        {
            modifiers |= Win32.MOD_WIN;
        }

        return modifiers;
    }

    private static bool IsDown(int virtualKey) => (Win32.GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    // ================================================================ 消息线程

    private void StartMessageThread()
    {
        _messageThread = new Thread(MessageThreadMain)
        {
            IsBackground = true,
            Name = "MortarHUD.HotkeyPump",
        };

        _messageThread.Start();
        _ready.Wait(TimeSpan.FromSeconds(5));
    }

    private void MessageThreadMain()
    {
        _className = $"MortarHUD.InputWindow.{Environment.ProcessId}.{Guid.NewGuid():N}";
        var hInstance = Win32.GetModuleHandle(null);

        var wndClass = new Win32.WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<Win32.WNDCLASSEXW>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate),
            hInstance = hInstance,
            lpszClassName = _className,
        };

        _classAtom = Win32.RegisterClassEx(ref wndClass);
        if (_classAtom == 0)
        {
            _ready.Set();
            return;
        }

        // 注意这里不能用 HWND_MESSAGE：message-only 窗口收不到原始输入。
        // 用一个从不显示的 0 尺寸弹出窗口，WS_EX_TOOLWINDOW 保证它不进 Alt+Tab。
        _hwnd = Win32.CreateWindowEx(
            Win32.WS_EX_TOOLWINDOW, _className, "MortarHUD.Input", Win32.WS_POPUP,
            0, 0, 0, 0,
            IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);

        _ready.Set();

        if (_hwnd == IntPtr.Zero)
        {
            Win32.UnregisterClass(_className, hInstance);
            return;
        }

        while (true)
        {
            // GetMessage 返回 0 表示收到 WM_QUIT，返回 -1 表示出错。
            var result = Win32.GetMessage(out var msg, IntPtr.Zero, 0, 0);
            if (result == 0 || result == -1)
            {
                break;
            }

            Win32.TranslateMessage(ref msg);
            Win32.DispatchMessage(ref msg);
        }

        Win32.DestroyWindow(_hwnd);
        Win32.UnregisterClass(_className, hInstance);
        _hwnd = IntPtr.Zero;
    }

    private IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case RawInput.WM_INPUT:
                HandleRawInput(lParam);
                // 前台 WM_INPUT 需要交给 DefWindowProc 清理系统的输入缓冲。
                return Win32.DefWindowProc(hWnd, msg, wParam, lParam);

            case WmRaiseHotkey:
            {
                while (_pendingHotkeys.TryDequeue(out var pending))
                {
                    if (_actionToDefinition.TryGetValue(pending, out var definition))
                    {
                        HotkeyPressed?.Invoke(this, new HotkeyPressedEventArgs(pending, definition));
                    }
                    else
                    {
                        Log?.Invoke($"热键事件 {pending} 没有对应的绑定，已丢弃");
                    }
                }

                return IntPtr.Zero;
            }

            case Win32.WM_HOTKEY:
            {
                var id = wParam.ToInt32();
                if (_idToAction.TryGetValue(id, out var action)
                    && _actionToDefinition.TryGetValue(action, out var definition))
                {
                    HotkeyPressed?.Invoke(this, new HotkeyPressedEventArgs(action, definition));
                }

                return IntPtr.Zero;
            }

            case WmExecuteQueue:
            {
                // 把注册/注销请求拉回消息线程执行，保证与窗口同线程。
                while (_workQueue.TryDequeue(out var work))
                {
                    try
                    {
                        work();
                    }
                    catch (Exception ex)
                    {
                        Log?.Invoke($"热键队列任务异常：{ex.Message}");
                    }
                }

                return IntPtr.Zero;
            }

            case Win32.WM_DESTROY:
                Win32.PostQuitMessage(0);
                return IntPtr.Zero;

            default:
                return Win32.DefWindowProc(hWnd, msg, wParam, lParam);
        }
    }

    private void RunOnMessageThread(Action action)
    {
        if (_hwnd == IntPtr.Zero || _messageThread is null || Thread.CurrentThread == _messageThread)
        {
            action();
            return;
        }

        using var done = new ManualResetEventSlim(false);
        Exception? captured = null;

        _workQueue.Enqueue(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                captured = ex;
            }
            finally
            {
                done.Set();
            }
        });

        Win32.PostMessage(_hwnd, WmExecuteQueue, IntPtr.Zero, IntPtr.Zero);

        if (!done.Wait(TimeSpan.FromSeconds(5)))
        {
            // 之前这里是「超时就当没事发生」，结果是热键静默失效、日志里一个字都没有。
            // 宁可抛出去让调用方看见。
            throw new TimeoutException(
                Loc.T("TheHotkeyMessageThreadDidNotRespondWithin5Second"));
        }

        if (captured is not null)
        {
            throw new InvalidOperationException(Loc.T("TheHotkeyOperationFailedOnTheMessageThread"), captured);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        UnregisterAll();

        if (_hwnd != IntPtr.Zero)
        {
            Win32.PostMessage(_hwnd, Win32.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        }

        _messageThread?.Join(TimeSpan.FromSeconds(2));
        _ready.Dispose();
        GC.SuppressFinalize(this);
    }
}
