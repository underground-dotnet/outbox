using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Underground.Outbox.Configuration;
using Underground.Outbox.Data;
using Underground.Outbox.Domain;
using Underground.OutboxTest.TestHandler;

namespace Underground.OutboxTest.Domain;

/// <summary>
/// Every Handler gets a bounded amount of time. These tests are about what happens when it runs out:
/// the token the Handler was given fires, the attempt is recorded like any other failure, and the
/// worker is free again rather than held for good.
/// </summary>
[Collection("ExampleMessageHandler Collection")]
public class HandlerTimeoutTests : DatabaseTest
{
    private readonly ITestOutputHelper _testOutputHelper;

    public HandlerTimeoutTests(ITestOutputHelper testOutputHelper) : base(testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;

        BlockingMessageHandler.Reset();
    }

    /// <summary>
    /// One worker driven on the calling thread, so that reaching the second Group is only possible if the
    /// first Handler was given up on rather than waited out.
    /// </summary>
    [Fact]
    public async Task HandlerThatNeverReturnsIsCancelledAndItsMessageBacksOff()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var serviceProvider = BuildServiceProvider(cfg =>
        {
            cfg.AddHandler<BlockingMessageHandler, BlockingMessage>();
            cfg.HandlerTimeout = TimeSpan.FromMilliseconds(250);

            // long enough that the timed-out message stays out of sight for the rest of this run
            cfg.BackoffBase = TimeSpan.FromMinutes(10);
            cfg.BackoffJitter = 0;
        });
        var processor = serviceProvider.GetRequiredService<ConcurrentProcessor<OutboxMessage>>();
        var context = CreateDbContext();

        const int hung = 1;
        const int other = 2;
        BlockingMessageHandler.BlockingIds.Add(hung);
        await context.AddMessagesAsync(
            serviceProvider,
            [MessageFor(hung, "hung"), MessageFor(other, "other")],
            cancellationToken);

        // Act: nothing releases the blocking Handler, so it returns only by being cancelled
        await processor.ProcessUntilIdleAsync(cancellationToken);

        // Assert
        var messages = await context.OutboxMessages.AsNoTracking().ToListAsync(cancellationToken);
        var hungMessage = messages.Single(m => string.Equals(m.GroupKey, "hung", StringComparison.Ordinal));
        var otherMessage = messages.Single(m => string.Equals(m.GroupKey, "other", StringComparison.Ordinal));

        // the token the Handler was given is what ended it: the Handler's own escape hatch is forty times
        // the configured timeout, and elapsing raises something other than a cancellation
        Assert.True(BlockingMessageHandler.WasCancelled, "the handler was never cancelled");

        Assert.Null(hungMessage.ProcessedAt);
        Assert.Equal(1, hungMessage.RetryCount);

        // pushed out of sight by the backoff, exactly as a Handler that threw would have been
        var secondsUntilVisible = await context.SecondsUntilVisibleAsync(hungMessage.Id, cancellationToken);
        Assert.InRange(secondsUntilVisible, TimeSpan.FromMinutes(9).TotalSeconds, TimeSpan.FromMinutes(10).TotalSeconds);

        // the worker went on to the other Group instead of staying inside the hung Handler
        Assert.Contains(other, BlockingMessageHandler.CalledWith);
        Assert.NotNull(otherMessage.ProcessedAt);
    }

    /// <summary>
    /// The other direction, so that the stage is pinned against cancelling too eagerly as well as too
    /// late: a Handler that takes its time but returns inside its budget is handled, and the token it was
    /// given is never cancelled.
    /// </summary>
    [Fact]
    public async Task HandlerThatReturnsWithinItsTimeIsNeitherCancelledNorRetried()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var serviceProvider = BuildServiceProvider(cfg =>
        {
            cfg.AddHandler<BlockingMessageHandler, BlockingMessage>();
            cfg.HandlerTimeout = TimeSpan.FromSeconds(30);
        });
        var processor = serviceProvider.GetRequiredService<ConcurrentProcessor<OutboxMessage>>();
        var context = CreateDbContext();

        const int slow = 1;
        BlockingMessageHandler.BlockingIds.Add(slow);
        await context.AddMessagesAsync(serviceProvider, [MessageFor(slow, "slow")], cancellationToken);

        // Act: the Handler really does sit inside the budget rather than returning at once, and is let go
        // well before the budget is up
        var run = processor.ProcessUntilIdleAsync(cancellationToken);
        await BlockingMessageHandler.Blocked.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        BlockingMessageHandler.Release.TrySetResult();
        await run;

        // Assert
        Assert.False(BlockingMessageHandler.WasCancelled, "the handler was cancelled inside its budget");

        var message = await context.OutboxMessages.AsNoTracking().SingleAsync(cancellationToken);
        Assert.NotNull(message.ProcessedAt);
        Assert.Equal(0, message.RetryCount);
    }

    private ServiceProvider BuildServiceProvider(Action<OutboxServiceConfiguration> configure)
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddOutboxServices<TestDbContext>(configure);
        serviceCollection.AddBaseServices(Container, _testOutputHelper);

        return serviceCollection.BuildServiceProvider();
    }

    private static OutboxMessage MessageFor(int id, string groupKey) =>
        new(Guid.NewGuid(), DateTime.UtcNow, new BlockingMessage(id), groupKey: groupKey);
}
