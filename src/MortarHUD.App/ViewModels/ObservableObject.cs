using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MortarHUD.App.ViewModels;

/// <summary>极简的 <see cref="INotifyPropertyChanged"/> 基类。</summary>
/// <remarks>
/// 没上 MVVM 框架：这个项目需要通知的属性不到一百个，
/// 引一个框架带来的约定和依赖比手写这一小段代码更麻烦。
/// </remarks>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void RaisePropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    /// <summary>值确实变化时才赋值并通知，返回是否发生了变化。</summary>
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        RaisePropertyChanged(propertyName);
        return true;
    }
}
