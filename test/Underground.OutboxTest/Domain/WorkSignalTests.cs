using Underground.Outbox.Domain;

namespace Underground.OutboxTest.Domain;

/// <summary>
/// The wake-up mechanism on its own, without a database. Every wait here is given a timeout far longer
/// than the assertion that surrounds it, so that a signal which fails to release its waiters fails the
/// test rather than passing slowly on the poll delay.
/// </summary>
public class WorkSignalTests
{
    private static readonly TimeSpan NeverInThisTest = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan LongEnoughToBeConclusive = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Whether a wait finished at all, without observing how it finished. A wait still running after this
    /// long is one that was never released, which is what every test here is really asking about.
    /// </summary>
    private static async Task<bool> WasReleasedAsync(Task wait, CancellationToken cancellationToken)
    {
        var finished = await Task.WhenAny(wait, Task.Delay(LongEnoughToBeConclusive, cancellationToken)).ConfigureAwait(false);

        return finished == wait;
    }

    [Fact]
    public async Task NotifyingWhileNobodyWaitsStillReleasesTheNextWaiter()
    {
        var signal = new WorkSignal();

        // the notification a worker races with: it lands after that worker found no work and before it
        // started waiting, so nothing is parked to receive it
        signal.Notify();

        var wait = signal.WaitAsync(NeverInThisTest, TestContext.Current.CancellationToken);

        Assert.True(
            await WasReleasedAsync(wait, TestContext.Current.CancellationToken),
            "the notification was dropped, so the waiter is left running to its poll timeout");
    }

    [Fact]
    public async Task OneNotificationReleasesEveryWaiter()
    {
        var signal = new WorkSignal();

        var waits = Enumerable.Range(0, 4)
            .Select(_ => signal.WaitAsync(NeverInThisTest, TestContext.Current.CancellationToken))
            .ToList();

        // there is no way to observe that a waiter has parked, and a notification that arrives before
        // they all have would be drained by the first one; this is the one timing-dependent test here
        await Task.Delay(TimeSpan.FromMilliseconds(250), TestContext.Current.CancellationToken);

        signal.Notify();

        Assert.True(
            await WasReleasedAsync(Task.WhenAll(waits), TestContext.Current.CancellationToken),
            "only some waiters were released, so a single commit would put only part of the pool back to work");
    }

    [Fact]
    public async Task WaitingGivesUpAfterTheTimeoutWithoutAnyNotification()
    {
        var signal = new WorkSignal();

        var wait = signal.WaitAsync(TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken);

        Assert.True(
            await WasReleasedAsync(wait, TestContext.Current.CancellationToken),
            "polling is what guarantees delivery, so a wait nobody notified must still end");
    }

    [Fact]
    public async Task CancellingAWaitReturnsRatherThanThrowing()
    {
        var signal = new WorkSignal();
        using var shutdown = new CancellationTokenSource();

        var wait = signal.WaitAsync(NeverInThisTest, shutdown.Token);
        await shutdown.CancelAsync();

        Assert.True(await WasReleasedAsync(wait, TestContext.Current.CancellationToken), "the wait ignored the cancellation");

        // the worker loop decides what a cancellation means, so the wait itself must not throw
        Assert.True(wait.IsCompletedSuccessfully);
    }
}
