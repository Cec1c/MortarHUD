using System.Runtime.InteropServices;

namespace MortarHUD.Platform.Windows.NativeMethods;

/// <summary>
/// 原始输入（Raw Input）互操作。
/// </summary>
/// <remarks>
/// <para>
/// 用来「观察」鼠标键和键盘键，<strong>不拦截</strong>——被观察的键照常送到游戏手里。
/// </para>
/// <para>
/// 为什么不用低级钩子（WH_MOUSE_LL / WH_KEYBOARD_LL）：
/// Windows 对钩子回调有硬性超时（默认 300ms），一旦某次回调超时，
/// 系统会不报错、不通知地把钩子摘掉，而且永不恢复。
/// 用户看到的就是「刚启动时好使，用着用着彻底没反应」。
/// 原始输入是内核直接把事件投递到窗口消息队列，没有回调，也就没有超时这回事。
/// </para>
/// </remarks>
internal static partial class RawInput
{
    internal const int WM_INPUT = 0x00FF;

    /// <summary>即使本进程不是前台窗口也接收输入。</summary>
    internal const uint RIDEV_INPUTSINK = 0x00000100;

    internal const uint RID_INPUT = 0x10000003;

    internal const uint RIM_TYPEMOUSE = 0;
    internal const uint RIM_TYPEKEYBOARD = 1;

    internal const ushort HID_USAGE_PAGE_GENERIC = 0x01;
    internal const ushort HID_USAGE_GENERIC_MOUSE = 0x02;
    internal const ushort HID_USAGE_GENERIC_KEYBOARD = 0x06;

    // 鼠标按键位（usButtonFlags）
    internal const ushort RI_MOUSE_LEFT_BUTTON_DOWN = 0x0001;
    internal const ushort RI_MOUSE_RIGHT_BUTTON_DOWN = 0x0004;
    internal const ushort RI_MOUSE_MIDDLE_BUTTON_DOWN = 0x0010;
    internal const ushort RI_MOUSE_BUTTON_4_DOWN = 0x0040;
    internal const ushort RI_MOUSE_BUTTON_5_DOWN = 0x0080;

    [StructLayout(LayoutKind.Sequential)]
    internal struct RAWINPUTDEVICE
    {
        public ushort usUsagePage;
        public ushort usUsage;
        public uint dwFlags;
        public IntPtr hwndTarget;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RAWINPUTHEADER
    {
        public uint dwType;
        public uint dwSize;
        public IntPtr hDevice;
        public IntPtr wParam;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RegisterRawInputDevices(
        RAWINPUTDEVICE[] pRawInputDevices, uint uiNumDevices, uint cbSize);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint GetRawInputData(
        IntPtr hRawInput, uint uiCommand, IntPtr pData, ref uint pcbSize, uint cbSizeHeader);

    /// <summary>
    /// <c>RAWINPUT</c> 里联合体的偏移量。
    /// </summary>
    /// <remarks>
    /// 结构体带匿名联合体，用 Explicit 布局手写既啰嗦又容易错，
    /// 这里按 ABI 直接算偏移（x64 下 header 恒为 24 字节）：
    /// <code>
    /// RAWKEYBOARD: MakeCode(2) Flags(2) Reserved(2) VKey(2) Message(4) Extra(4)
    ///                                          ^ 偏移 6
    ///
    /// RAWMOUSE:    usFlags(2) [pad(2)] ulButtons(4) ulRawButtons(4) lLastX(4) lLastY(4) ulExtra(4)
    ///                                  ^ usButtonFlags 偏移 4、usButtonData 偏移 6
    /// </code>
    /// </remarks>
    internal const int KeyboardVKeyOffset = 6;

    internal const int MouseButtonFlagsOffset = 4;

    /// <summary>读一个 16 位无符号数。</summary>
    internal static ushort ReadUInt16(IntPtr buffer, int offset)
        => (ushort)Marshal.ReadInt16(buffer, offset);

    internal static uint ReadUInt32(IntPtr buffer, int offset)
        => (uint)Marshal.ReadInt32(buffer, offset);
}
