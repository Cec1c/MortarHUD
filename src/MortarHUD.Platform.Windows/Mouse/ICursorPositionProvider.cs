namespace MortarHUD.Platform.Windows.Mouse;

/// <summary>
/// 光标位置来源。
/// </summary>
/// <remarks>
/// 鼠标<strong>不是</strong>测量工具（TDD §10.1）——它只告诉程序
/// 「游戏此刻把 x/y 读数画在了屏幕的哪个位置」。
/// </remarks>
public interface ICursorPositionProvider
{
    /// <summary>
    /// 当前光标的物理像素坐标。
    /// </summary>
    /// <returns>读取失败时返回 false，调用方应报 CAPTURE FAILED 而不是用 (0,0)。</returns>
    bool TryGetCursorPosition(out int physicalX, out int physicalY);
}
