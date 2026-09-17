namespace MortarHUD.Platform.Windows.Hotkeys;

/// <summary>Raw Input 同时报告按下、长按和松开；只把新的按下作为操作。</summary>
internal sealed class ObservedKeyState
{
    private readonly HashSet<(IntPtr Device, ushort Key)> _down = [];

    internal bool Press(IntPtr device, ushort key, ushort flags)
    {
        if ((flags & 1) != 0)
        {
            _down.Remove((device, key));
            return false;
        }
        return key != 255 && _down.Add((device, key));
    }

    internal void Clear() => _down.Clear();
}
