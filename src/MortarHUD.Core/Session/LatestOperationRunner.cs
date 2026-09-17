namespace MortarHUD.Core.Session;

/// <summary>新操作替代旧操作，原生识别退出之前不释放资源或启动下一轮。</summary>
public sealed class LatestOperationRunner
{
    private readonly object _sync = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _current;
    private bool _cancelOnActivity;

    public void CancelOnActivity()
    {
        lock (_sync) { if (_cancelOnActivity) _current?.Cancel(); }
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

    public async Task RunAsync(Func<CancellationToken, Task> operation, bool cancelOnActivity = true,
        bool throwOnCancellation = false)
    {
        CancellationTokenSource source;
        lock (_sync)
        {
            _current?.Cancel();
            _current = source = new CancellationTokenSource();
            _cancelOnActivity = cancelOnActivity;
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
            // 被后续操作替代不是识别失败，不能覆盖 HUD 状态。
        }
        finally
        {
            if (entered) _gate.Release();
            lock (_sync)
            {
                if (ReferenceEquals(_current, source)) _current = null;
                source.Dispose();
            }
        }
    }
}
