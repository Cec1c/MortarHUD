using MortarHUD.Core.Session;
using Xunit;

namespace MortarHUD.Core.Tests;

public class LatestOperationRunnerTests
{
    [Fact]
    public async Task FrozenFramesAllowMotionButKeysClicksAndScrollStillCancel()
    {
        var runner = new LatestOperationRunner();
        await runner.RunAsync(token =>
        {
            runner.SealFrames(token);
            runner.CancelOnActivity(pointerMotion: true);
            Assert.True(runner.TryCommit(token, () => { }));
            runner.CancelOnActivity();
            Assert.False(runner.TryCommit(token, () => Assert.Fail("点击后不得提交旧截图")));
            return Task.CompletedTask;
        });
        await runner.RunAsync(token =>
        {
            runner.CancelOnActivity(pointerMotion: true);
            Assert.True(token.IsCancellationRequested);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task ActivityThenNewRequestDoesNotShowAnOldCancellationMessage()
    {
        var runner = new LatestOperationRunner();
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var discarded = 0;
        var old = runner.RunAsync(async token => { await pending.Task; token.ThrowIfCancellationRequested(); },
            onDiscarded: () => discarded++);
        runner.CancelOnActivity();
        var newer = runner.RunAsync(_ => Task.CompletedTask);
        pending.SetResult();
        await Task.WhenAll(old, newer);
        Assert.Equal(0, discarded);
    }

    [Fact]
    public async Task InputDoesNotCancelQueuedSettingsApplication()
    {
        var runner = new LatestOperationRunner();
        await runner.RunAsync(token =>
        {
            runner.CancelOnActivity();
            Assert.False(token.IsCancellationRequested);
            return Task.CompletedTask;
        }, cancelOnActivity: false);
    }

    [Fact]
    public async Task LateNativeResultCannotOverwriteNewRequest()
    {
        var runner = new LatestOperationRunner();
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writes = new List<int>();
        var old = runner.RunAsync(async token =>
        {
            await pending.Task; // 模拟不支持中断的原生 OCR。
            Assert.False(runner.TryCommit(token, () => writes.Add(1)));
        });
        var latest = runner.RunAsync(token =>
        {
            Assert.True(runner.TryCommit(token, () => writes.Add(2)));
            return Task.CompletedTask;
        });
        Assert.False(latest.IsCompleted);
        pending.SetResult();
        await Task.WhenAll(old, latest);
        Assert.Equal(new[] { 2 }, writes);
    }

    [Fact]
    public async Task CancellationAndSupersessionDiscardQueuedWork()
    {
        var runner = new LatestOperationRunner();
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = runner.RunAsync(async token =>
        {
            await pending.Task;
            Assert.False(runner.TryCommit(token, () => Assert.Fail("旧结果不得提交")));
        });
        var skipped = runner.RunAsync(_ => { Assert.Fail("过期队列不得启动"); return Task.CompletedTask; });
        var last = runner.RunAsync(_ => Task.CompletedTask);
        runner.Cancel();
        pending.SetResult();
        await Task.WhenAll(first, skipped, last);
        await runner.RunAsync(token =>
        {
            Assert.True(runner.TryCommit(token, () => { }));
            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// 用户动鼠标造成的取消必须报出来。
    /// </summary>
    /// <remarks>
    /// 实机日志里 140 次采集有 23 次被静默丢弃，用户看到的就是「按了键没反应」。
    /// 取消本身是对的（不许用错数据），错的是不吭声。
    /// </remarks>
    [Fact]
    public async Task ActivityCancellation_ReportsDiscarded()
    {
        var runner = new LatestOperationRunner();
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var discarded = 0;

        var running = runner.RunAsync(async token =>
        {
            await pending.Task;
            token.ThrowIfCancellationRequested();
        }, onDiscarded: () => Interlocked.Increment(ref discarded));

        runner.CancelOnActivity();
        pending.SetResult();
        await running;

        Assert.Equal(1, discarded);
    }

    /// <summary>被后续操作替代不是「用户动了鼠标」，不该打扰用户。</summary>
    [Fact]
    public async Task Supersession_DoesNotReportDiscarded()
    {
        var runner = new LatestOperationRunner();
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var discarded = 0;

        var old = runner.RunAsync(async token =>
        {
            await pending.Task;
            token.ThrowIfCancellationRequested();
        }, onDiscarded: () => Interlocked.Increment(ref discarded));

        var latest = runner.RunAsync(
            _ => Task.CompletedTask, onDiscarded: () => Interlocked.Increment(ref discarded));

        pending.SetResult();
        await Task.WhenAll(old, latest);

        Assert.Equal(0, discarded);
    }

    [Fact]
    public async Task ExceptionReleasesGate()
    {
        var runner = new LatestOperationRunner();
        await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync(_ => throw new InvalidOperationException()));
        await runner.RunAsync(token => { Assert.True(runner.TryCommit(token, () => { })); return Task.CompletedTask; });
    }
}
