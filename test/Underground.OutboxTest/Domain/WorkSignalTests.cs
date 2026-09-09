using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Underground.Outbox.Configuration;
using Underground.Outbox.Data;
using Underground.Outbox.Domain;

namespace Underground.OutboxTest.Domain;

/// <summary>
/// The wake-up mechanism on its own, without a database. Nothing here ends a wait but a notification or a
/// cancellation, so a signal that fails to release its waiters fails the test rather than passing slowly on
/// the poll cadence.
/// </summary>
public class WorkSignalTests
{
    private static readonly TimeSpan LongEnoughToBeConclusive = TimeSpan.FromSeconds(5);

    /// <summary>
    /// A processor built for its signal alone. Nothing on the notify/wait path touches the scope factory or
    /// anything on the configuration, but both are real rather than null so that a change which starts
    /// reaching for them fails as a test rather than as a null reference.
    /// </summary>
    private static ConcurrentProcessor<TestDbContext, OutboxMessage> CreateProcessor()
    {
        var scopeFactory = new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        return new ConcurrentProcessor<TestDbContext, OutboxMessage>(
            NullLogger<ConcurrentProcessor<TestDbContext, OutboxMessage>>.Instance,
            scopeFactory,
            new OutboxServiceConfiguration<TestDbContext>());
    }

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
        var processor = CreateProcessor();

        // the notification a worker races with: it lands after that worker found no work and before it
        // started waiting, so nothing is parked to receive it
        processor.NotifyWork();

        var wait = processor.WaitForWorkAsync(TestContext.Current.CancellationToken);

        Assert.True(
            await WasReleasedAsync(wait, TestContext.Current.CancellationToken),
            "the notification was dropped, so the waiter is left running to the next poll");
    }

    [Fact]
    public async Task OneNotificationReleasesEveryWaiter()
    {
        var processor = CreateProcessor();

        var waits = Enumerable.Range(0, 4)
            .Select(_ => processor.WaitForWorkAsync(TestContext.Current.CancellationToken))
            .ToList();

        // there is no way to observe that a waiter has parked, and a notification that arrives before
        // they all have would be drained by the first one; this is the one timing-dependent test here
        await Task.Delay(TimeSpan.FromMilliseconds(250), TestContext.Current.CancellationToken);

        processor.NotifyWork();

        Assert.True(
            await WasReleasedAsync(Task.WhenAll(waits), TestContext.Current.CancellationToken),
            "only some waiters were released, so a single commit would put only part of the pool back to work");
    }

    [Fact]
    public async Task CancellingAWaitReturnsRatherThanThrowing()
    {
        var processor = CreateProcessor();
        using var shutdown = new CancellationTokenSource();

        var wait = processor.WaitForWorkAsync(shutdown.Token);
        await shutdown.CancelAsync();

        Assert.True(await WasReleasedAsync(wait, TestContext.Current.CancellationToken), "the wait ignored the cancellation");

        // the worker loop decides what a cancellation means, so the wait itself must not throw
        Assert.True(wait.IsCompletedSuccessfully);
    }
}
