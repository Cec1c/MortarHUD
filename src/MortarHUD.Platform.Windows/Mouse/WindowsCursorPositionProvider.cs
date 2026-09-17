using MortarHUD.Platform.Windows.NativeMethods;

namespace MortarHUD.Platform.Windows.Mouse;

/// <summary>
/// 用 <c>GetCursorPos</c> 取光标位置。
/// </summary>
/// <remarks>
/// 进程声明了 Per-Monitor V2 之后，该 API 返回的就是物理像素，
/// 与 BitBlt 截屏所用的坐标空间一致（TDD §12）。
/// </remarks>
public sealed class WindowsCursorPositionProvider : ICursorPositionProvider
{
    public bool TryGetCursorPosition(out int physicalX, out int physicalY)
    {
        if (Win32.GetCursorPos(out var point))
        {
            physicalX = point.X;
            physicalY = point.Y;
            return true;
        }

        physicalX = 0;
        physicalY = 0;
        return false;
    }
}
