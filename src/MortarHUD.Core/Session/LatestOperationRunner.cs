namespace MortarHUD.Core.Session;

/// <summary>新操作替代旧操作，原生识别退出之前不释放资源或启动下一轮。</summary>
public sealed class LatestOperationRunner
{
    private readonly object _sync = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _current;
    private CancellationTokenSource? _activityCancelled;
    private bool _cancelOnActivity;
    private bool _framesSealed;

    public void CancelOnActivity(bool pointerMotion = false)
    {
        lock (_sync)
        {
            if (!_cancelOnActivity || _current is null || (pointerMotion && _framesSealed))
            {
                return;
            }

            // 记下「这次取消是用户活动造成的」。它和被新操作替代是两回事：
            // 被替代时用户本来就在做下一个动作，静默是对的；
            // 用户动鼠标导致的取消必须给提示，否则表现就是「按了键没反应」。
            _activityCancelled = _current;
            _current.Cancel();
        }
    }

    /// <summary>只放行截图之后的鼠标移动；点击、滚轮、键盘和新请求仍然取消。</summary>
    public void SealFrames(CancellationToken token)
    {
        lock (_sync)
        {
            if (_current?.Token == token && !token.IsCancellationRequested) _framesSealed = true;
        }
    }

    public void Cancel()
    {
        lock (_sync)
        {
            _current?.Cancel();
        }
    }

    public bool TryCommit(CancellationToken token, Action commit)
    {
        lock (_sync)
        {
            if (_current is null || _current.Token != token || token.IsCancellationRequested)
                return false;
            commit();
            return true;
        }
    }

    /// <param name="operation">要执行的操作。</param>
    /// <param name="cancelOnActivity">用户活动（移动鼠标 / 滚轮）是否取消本次操作。</param>
    /// <param name="throwOnCancellation">取消时是否抛出去而不是吞掉。</param>
    /// <param name="onDiscarded">
    /// 因<strong>用户活动</strong>而作废时的回调，用来给用户一个可见提示。
    /// 被后续操作替代<strong>不</strong>回调——那种情况不需要打扰用户。
    /// </param>
    public async Task RunAsync(Func<CancellationToken, Task> operation, bool cancelOnActivity = true,
        bool throwOnCancellation = false, Action? onDiscarded = null)
    {
        CancellationTokenSource source;
        lock (_sync)
        {
            _current?.Cancel();
            _current = source = new CancellationTokenSource();
            _cancelOnActivity = cancelOnActivity;
            _framesSealed = false;
        }

        var entered = false;
        try
        {
            await _gate.WaitAsync(source.Token);
            entered = true;
            source.Token.ThrowIfCancellationRequested();
            await operation(source.Token);
        }
        catch (OperationCanceledException) when (source.IsCancellationRequested && !throwOnCancellation)
        {
            // 被后续操作替代不是识别失败，不能覆盖 HUD 状态；
            // 但用户活动造成的取消要报出去，不能让它继续静默。
            bool byActivity;
            lock (_sync)
            {
                byActivity = ReferenceEquals(_activityCancelled, source) && ReferenceEquals(_current, source);
            }

            if (byActivity)
            {
                onDiscarded?.Invoke();
            }
        }
        finally
        {
            if (entered) _gate.Release();
            lock (_sync)
            {
                if (ReferenceEquals(_current, source)) _current = null;
                if (ReferenceEquals(_activityCancelled, source)) _activityCancelled = null;
                source.Dispose();
            }
        }
    }
}
