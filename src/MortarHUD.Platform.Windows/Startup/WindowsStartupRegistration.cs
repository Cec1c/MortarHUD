using System.Runtime.Versioning;
using MortarHUD.Localization;
using Microsoft.Win32;

namespace MortarHUD.Platform.Windows.Startup;

/// <summary>
/// 开机自启（TDD §28）。走当前用户的 Run 键，不需要管理员权限。
/// </summary>
/// <remarks>
/// 不用计划任务也不用服务：这个程序就是一个随游戏一起开的辅助工具，
/// 用户能随时在设置里关掉才是对的。
/// </remarks>
public static class WindowsStartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private const string ValueName = "MortarHUD";

    /// <summary>当前是否已经登记为开机自启。</summary>
    [SupportedOSPlatform("windows")]
    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) is string value && value.Length > 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    /// <summary>登记 / 取消开机自启。</summary>
    /// <returns>成功返回 true；失败时 <paramref name="error"/> 给出原因。</returns>
    [SupportedOSPlatform("windows")]
    public static bool SetEnabled(bool enabled, out string? error)
    {
        error = null;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);

            if (key is null)
            {
                error = "无法打开注册表 Run 键。";
                return false;
            }

            if (enabled)
            {
                var executable = Environment.ProcessPath;

                if (string.IsNullOrEmpty(executable))
                {
                    error = Loc.T("CannotDetermineTheApplicationSOwnPath");
                    return false;
                }

                // 加引号：路径里带空格时（Program Files）不加会被截断成错误的命令行。
                key.SetValue(ValueName, $"\"{executable}\"");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            return true;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException
                                       or UnauthorizedAccessException
                                       or IOException)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>注册表里登记的可执行文件路径，没有则为 null。</summary>
    [SupportedOSPlatform("windows")]
    public static string? GetRegisteredCommand()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) as string;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
