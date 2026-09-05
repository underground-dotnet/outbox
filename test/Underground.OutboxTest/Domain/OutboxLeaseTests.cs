using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Underground.Outbox.Configuration;
using Underground.Outbox.Data;
using Underground.Outbox.Domain;
using Underground.OutboxTest.TestHandler;

namespace Underground.OutboxTest.Domain;

/// <summary>
/// An outbox worker holds its message with a Lease rather than with a row lock: it is granted for a
/// bounded time, it expires on its own, and every write the worker makes afterwards is guarded on it.
/// These tests are about the two things that buys - a dead worker no longer blocks its Group - and the
/// one thing it costs, which is that two workers can hold the same message in turn and only one of them
/// may write the outcome.
/// </summary>
[Collection("ExampleMessageHandler Collection")]
public class OutboxLeaseTests : DatabaseTest
{
    private readonly ITestOutputHelper _testOutputHelper;
    private readonly RecordingLoggerProvider _logs = new();

    public OutboxLeaseTests(ITestOutputHelper testOutputHelper) : base(testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;

        BlockingMessageHandler.Reset();
    }

    /// <summary>
    /// A worker that claimed a message and then died leaves nothing behind but the Lease. Until it
    /// expires the Group offers nothing - the message is its Head and the Head is out of sight - and once
    /// it does, the message is handled by whoever asks next.
    /// </summary>
    [Fact]
    public async Task MessageWhoseWorkerDiedIsOfferedAgainOnceItsLeaseExpires()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var serviceProvider = BuildServiceProvider(cfg => cfg.AddHandler<BlockingMessageHandler, BlockingMessage>());
        var processor = serviceProvider.GetRequiredService<ConcurrentProcessor<OutboxMessage>>();
        var context = CreateDbContext();

        await context.AddMessagesAsync(serviceProvider, [MessageFor(1, "group")], cancellationToken);

        // a worker that claims and is never heard from again: the claim is committed, so the Lease it
        // granted itself outlives it exactly as it would outlive a killed process
        var claimed = await ClaimAndAbandonAsync(serviceProvider, cancellationToken);
        Assert.NotNull(claimed);

        // Act & Assert: while the Lease runs, the Group offers nothing at all
        await processor.ProcessUntilIdleAsync(cancellationToken);
        Assert.Empty(BlockingMessageHandler.CalledWith);

        // the Lease expiring is the only thing that changes, and it is enough
        await context.MakeUnhandledMessagesVisibleAsync(cancellationToken);
        await processor.ProcessUntilIdleAsync(cancellationToken);

        Assert.Equal([1], BlockingMessageHandler.CalledWith);
        var message = await context.OutboxMessages.AsNoTracking().SingleAsync(cancellationToken);
        Assert.NotNull(message.ProcessedAt);
    }

    /// <summary>
    /// The completion guard, which is the whole reason every write carries the granted instant. A worker
    /// whose Lease expired while it was still inside its Handler must not be able to mark the message
    /// handled, and must not be able to push the second worker's claim out of the way - either would fan
    /// the message out to a third worker.
    /// </summary>
    [Fact]
    public async Task WorkerThatLostItsLeaseNeitherOverwritesTheNewClaimNorMarksTheMessageHandled()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var serviceProvider = BuildServiceProvider(cfg =>
        {
            cfg.AddHandler<BlockingMessageHandler, BlockingMessage>();

            // the Handler is held by the test rather than by the clock, so its budget only has to be
            // longer than the test takes
            cfg.HandlerTimeout = TimeSpan.FromSeconds(30);
        });
        var processor = serviceProvider.GetRequiredService<ConcurrentProcessor<OutboxMessage>>();
        var context = CreateDbContext();

        const int held = 1;
        BlockingMessageHandler.BlockingIds.Add(held);
        await context.AddMessagesAsync(serviceProvider, [MessageFor(held, "group")], cancellationToken);

        // Act: the first worker claims and is still inside its Handler
        var firstWorker = processor.ProcessNextAsync(cancellationToken);
        await BlockingMessageHandler.Blocked.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);

        // its Lease runs out while it is in there - the database decides all timing, so moving the column
        // is indistinguishable from having waited
        await context.MakeUnhandledMessagesVisibleAsync(cancellationToken);

        // a second worker takes the message, which is now free for anyone to claim
        var secondClaim = await ClaimAndAbandonAsync(serviceProvider, cancellationToken);
        Assert.NotNull(secondClaim);
        var secondLease = await context.VisibleAtAsync(secondClaim.Id, cancellationToken);

        // and only now does the first worker finish, successfully, believing the message to be its own
        BlockingMessageHandler.Release.TrySetResult();
        await firstWorker;

        // Assert
        var message = await context.OutboxMessages.AsNoTracking().SingleAsync(cancellationToken);

        // the first worker's completion write matched no row, so the message is still the second
        // worker's to finish rather than being marked handled behind its back
        Assert.Null(message.ProcessedAt);
        Assert.Equal(secondLease, await context.VisibleAtAsync(message.Id, cancellationToken));

        // reported rather than swallowed or thrown: an operator wants the rate of it, and the worker
        // carries on to its next message
        Assert.Contains(
            _logs.Entries,
            entry => entry.Level == LogLevel.Warning && entry.Message.Contains("Lost the Lease", StringComparison.Ordinal));
    }

    /// <summary>
    /// The other side of the guard: a Handler that runs right up to its budget still holds its Lease when
    /// it is cancelled, because the Lease is that budget plus a margin. A worker is never robbed of a
    /// message it is still working on.
    /// </summary>
    [Fact]
    public async Task HandlerThatTimesOutStillHoldsItsLeaseWhenItRecordsTheAttempt()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var serviceProvider = BuildServiceProvider(cfg =>
        {
            cfg.AddHandler<BlockingMessageHandler, BlockingMessage>();
            cfg.HandlerTimeout = TimeSpan.FromMilliseconds(250);
            cfg.BackoffBase = TimeSpan.FromMinutes(10);
            cfg.BackoffJitter = 0;
        });
        var processor = serviceProvider.GetRequiredService<ConcurrentProcessor<OutboxMessage>>();
        var context = CreateDbContext();

        const int hung = 1;
        BlockingMessageHandler.BlockingIds.Add(hung);
        await context.AddMessagesAsync(serviceProvider, [MessageFor(hung, "group")], cancellationToken);

        // Act: nothing releases the Handler, so it runs until its own budget is up
        await processor.ProcessUntilIdleAsync(cancellationToken);

        // Assert: the failure was recorded, which is only possible while the Lease still matched
        var message = await context.OutboxMessages.AsNoTracking().SingleAsync(cancellationToken);
        Assert.Equal(1, message.RetryCount);

        Assert.DoesNotContain(
            _logs.Entries,
            entry => entry.Message.Contains("Lost the Lease", StringComparison.Ordinal));
    }

    /// <summary>
    /// Claims one Head in its own committed transaction and does nothing else with it - the claim half of
    /// a worker, without the dispatch. That is both a worker that dies immediately after claiming and a
    /// second worker taking over an expired Lease, which is why the two tests share it.
    /// </summary>
    private static async Task<OutboxMessage?> ClaimAndAbandonAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IDbContext>();
        var claimHead = scope.ServiceProvider.GetRequiredService<ClaimHead<OutboxMessage>>();

        var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await using (transaction.ConfigureAwait(false))
        {
            var message = await claimHead.ExecuteAsync(cancellationToken);

            // committing is what turns the row lock into a Lease; without it the Group would stay blocked
            // rather than merely leased
            await transaction.CommitAsync(cancellationToken);

            return message;
        }
    }

    private ServiceProvider BuildServiceProvider(Action<OutboxServiceConfiguration> configure)
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddOutboxServices<TestDbContext>(configure);
        serviceCollection.AddBaseServices(Container, _testOutputHelper);
        serviceCollection.AddLogging(builder => builder.AddProvider(_logs));

        return serviceCollection.BuildServiceProvider();
    }

    private static OutboxMessage MessageFor(int id, string groupKey) =>
        new(Guid.NewGuid(), DateTime.UtcNow, new BlockingMessage(id), groupKey: groupKey);
}
