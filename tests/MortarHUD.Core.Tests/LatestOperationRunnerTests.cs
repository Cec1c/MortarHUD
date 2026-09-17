using MortarHUD.Core.Session;
using Xunit;

namespace MortarHUD.Core.Tests;

public class LatestOperationRunnerTests
{
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

    [Fact]
    public async Task ExceptionReleasesGate()
    {
        var runner = new LatestOperationRunner();
        await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync(_ => throw new InvalidOperationException()));
        await runner.RunAsync(token => { Assert.True(runner.TryCommit(token, () => { })); return Task.CompletedTask; });
    }
}
