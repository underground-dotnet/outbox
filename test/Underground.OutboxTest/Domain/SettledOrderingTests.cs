using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Underground.Outbox;
using Underground.Outbox.Configuration;
using Underground.Outbox.Data;
using Underground.Outbox.Domain;
using Underground.OutboxTest.TestHandler;

namespace Underground.OutboxTest.Domain;

/// <summary>
/// Ordering within a Group has to survive two of the application's own transactions writing to that
/// Group at the same time. Identity values are handed out when a row is inserted rather than when its
/// transaction commits, so these tests deliberately arrange for the identity order and the transaction
/// order to disagree - which is the only arrangement that can tell the two sort keys apart.
/// </summary>
[Collection("ExampleMessageHandler Collection")]
public class SettledOrderingTests : DatabaseTest
{
    private readonly IServiceProvider _serviceProvider;

    public SettledOrderingTests(ITestOutputHelper testOutputHelper) : base(testOutputHelper)
    {
        // clear the static lists to avoid interference between tests
        ExampleMessageHandler.CalledWith.Clear();
        ExampleMessageHandler.ObjectIds.Clear();

        var serviceCollection = new ServiceCollection();

        serviceCollection.AddOutboxServices<TestDbContext>(cfg => cfg.AddHandler<ExampleMessageHandler, ExampleMessage>());

        serviceCollection.AddBaseServices(Container, testOutputHelper);
        _serviceProvider = serviceCollection.BuildServiceProvider();
    }

    [Fact]
    public async Task MessagesAreHandledInTransactionStartOrderRatherThanInsertionOrder()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var outbox = _serviceProvider.GetRequiredService<IOutbox>();
        var processor = _serviceProvider.GetRequiredService<ConcurrentProcessor<OutboxMessage>>();

        await using var earlier = CreateDbContext();
        await using var later = CreateDbContext();

        var earlierTransaction = await earlier.Database.BeginTransactionAsync(cancellationToken);
        await using (earlierTransaction.ConfigureAwait(false))
        {
            // PostgreSQL assigns a transaction id at the first write, so make the earlier transaction take
            // one before the later transaction begins. Without this the two ids would be handed out in the
            // order the messages are inserted and the test could not fail.
            await earlier.Database.ExecuteSqlRawAsync("SELECT pg_current_xact_id()", cancellationToken);

            var laterTransaction = await later.Database.BeginTransactionAsync(cancellationToken);
            await using (laterTransaction.ConfigureAwait(false))
            {
                // the later transaction inserts first, so its message gets the lower Id ...
                await outbox.AddMessageAsync(later, MessageFor(2), cancellationToken);
                // ... while the earlier transaction's message gets the higher Id but the lower TransactionId
                await outbox.AddMessageAsync(earlier, MessageFor(1), cancellationToken);

                // commit in the opposite order to which the transactions started
                await laterTransaction.CommitAsync(cancellationToken);
            }

            await earlierTransaction.CommitAsync(cancellationToken);
        }

        // Act
        await processor.ProcessUntilIdleAsync(cancellationToken);

        // Assert
        Assert.Equal([new ExampleMessage(1), new ExampleMessage(2)], ExampleMessageHandler.CalledWith);
    }

    [Fact]
    public async Task MessageIsWithheldUntilNoOlderTransactionCouldStillInsertAheadOfIt()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var outbox = _serviceProvider.GetRequiredService<IOutbox>();
        var processor = _serviceProvider.GetRequiredService<ConcurrentProcessor<OutboxMessage>>();

        await using var earlier = CreateDbContext();
        await using var later = CreateDbContext();

        var earlierTransaction = await earlier.Database.BeginTransactionAsync(cancellationToken);
        await using (earlierTransaction.ConfigureAwait(false))
        {
            await outbox.AddMessageAsync(earlier, MessageFor(1), cancellationToken);

            // a whole transaction that starts and commits while the earlier one is still open
            var laterTransaction = await later.Database.BeginTransactionAsync(cancellationToken);
            await using (laterTransaction.ConfigureAwait(false))
            {
                await outbox.AddMessageAsync(later, MessageFor(2), cancellationToken);
                await laterTransaction.CommitAsync(cancellationToken);
            }

            // Act: the committed message is not offered yet, because the still-open transaction could
            // still insert an earlier message into the same Group - as it in fact already has.
            await processor.ProcessUntilIdleAsync(cancellationToken);

            // Assert
            Assert.Empty(ExampleMessageHandler.CalledWith);

            await earlierTransaction.CommitAsync(cancellationToken);
        }

        // Act
        await processor.ProcessUntilIdleAsync(cancellationToken);

        // Assert
        Assert.Equal([new ExampleMessage(1), new ExampleMessage(2)], ExampleMessageHandler.CalledWith);
    }

    private static OutboxMessage MessageFor(int id) => new(Guid.NewGuid(), DateTime.UtcNow, new ExampleMessage(id));
}
