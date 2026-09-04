using Microsoft.Extensions.DependencyInjection;

using Underground.Outbox.Configuration;
using Underground.Outbox.Data;
using Underground.Outbox.Domain;
using Underground.OutboxTest.TestHandler;

namespace Underground.OutboxTest.Domain;

/// <summary>
/// A Group offers only its Head - its oldest settled unhandled message - and it offers nothing at all
/// while that Head is not yet visible. These tests are the ones that tell the correct two-stage lookup
/// apart from the naive one that filters by visibility first: the naive query passes every test that
/// does not put a Head out of sight and then look at what happens to the messages behind it.
///
/// As elsewhere, the instant arriving is simulated by moving the stored value into the past rather than
/// by waiting.
/// </summary>
[Collection("ExampleMessageHandler Collection")]
public class HeadDiscoveryTests : DatabaseTest
{
    private static readonly TimeSpan ScheduledAhead = TimeSpan.FromMinutes(10);

    private const int Head = 1;
    private const int BehindTheHead = 2;
    private const int OtherGroupFirst = 3;
    private const int OtherGroupSecond = 4;

    private readonly ITestOutputHelper _testOutputHelper;

    public HeadDiscoveryTests(ITestOutputHelper testOutputHelper) : base(testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;

        RecoveringMessageHandler.CalledWith.Clear();
        RecoveringMessageHandler.FailingIds.Clear();
    }

    [Fact]
    public async Task MessagesBehindAHeadInBackoffAreNotHandledEvenThoughTheyAreVisible()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var serviceProvider = BuildServiceProvider();
        var processor = serviceProvider.GetRequiredService<ConcurrentProcessor<OutboxMessage>>();
        var context = CreateDbContext();
        RecoveringMessageHandler.FailingIds.Add(Head);
        await context.AddMessagesAsync(serviceProvider, [MessageFor(Head), MessageFor(BehindTheHead)], cancellationToken);

        // Act: the Head fails and goes into backoff, and every further run finds it still invisible
        await processor.ProcessUntilIdleAsync(cancellationToken);
        await processor.ProcessUntilIdleAsync(cancellationToken);
        await processor.ProcessUntilIdleAsync(cancellationToken);

        // Assert: the message behind the Head has been visible throughout and was still never offered
        Assert.Equal([Head], RecoveringMessageHandler.CalledWith);
    }

    [Fact]
    public async Task OtherGroupsAreHandledWhileOneGroupsHeadIsInBackoff()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var serviceProvider = BuildServiceProvider();
        var processor = serviceProvider.GetRequiredService<ConcurrentProcessor<OutboxMessage>>();
        var context = CreateDbContext();
        RecoveringMessageHandler.FailingIds.Add(Head);
        await context.AddMessagesAsync(
            serviceProvider,
            [
                MessageFor(Head, "stalled"),
                MessageFor(BehindTheHead, "stalled"),
                MessageFor(OtherGroupFirst, "healthy"),
                MessageFor(OtherGroupSecond, "healthy"),
            ],
            cancellationToken);

        // Act: the second run finds the stalled Group's Head still invisible and the healthy one empty
        await processor.ProcessUntilIdleAsync(cancellationToken);
        await processor.ProcessUntilIdleAsync(cancellationToken);

        // Assert: the healthy Group ran to completion ...
        Assert.Equal(
            [OtherGroupFirst, OtherGroupSecond],
            RecoveringMessageHandler.CalledWith.Where(id => id is OtherGroupFirst or OtherGroupSecond));
        // ... while the stalled Group offered its Head once and never the message behind it
        Assert.Equal(
            [Head],
            RecoveringMessageHandler.CalledWith.Where(id => id is Head or BehindTheHead));
    }

    [Fact]
    public async Task HeadIsHandledBeforeTheMessagesBehindItOnceItsBackoffHasElapsed()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var serviceProvider = BuildServiceProvider();
        var processor = serviceProvider.GetRequiredService<ConcurrentProcessor<OutboxMessage>>();
        var context = CreateDbContext();
        RecoveringMessageHandler.FailingIds.Add(Head);
        await context.AddMessagesAsync(serviceProvider, [MessageFor(Head), MessageFor(BehindTheHead)], cancellationToken);

        await processor.ProcessUntilIdleAsync(cancellationToken);
        // a further run while the Head is in backoff must not reach the message behind it either
        await processor.ProcessUntilIdleAsync(cancellationToken);
        var whileInBackoff = RecoveringMessageHandler.CalledWith.ToList();

        // Act: the partner system recovers and the backoff elapses
        RecoveringMessageHandler.FailingIds.Clear();
        await context.MakeUnhandledMessagesVisibleAsync(cancellationToken);
        await processor.ProcessUntilIdleAsync(cancellationToken);

        // Assert
        Assert.Equal([Head], whileInBackoff);
        Assert.Equal([Head, Head, BehindTheHead], RecoveringMessageHandler.CalledWith);
    }

    [Fact]
    public async Task MessagesBehindAScheduledHeadAreNotHandledUntilThatHeadHasBeen()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var serviceProvider = BuildServiceProvider();
        var processor = serviceProvider.GetRequiredService<ConcurrentProcessor<OutboxMessage>>();
        var context = CreateDbContext();
        var scheduledHead = MessageFor(Head, visibleAt: DateTime.UtcNow.Add(ScheduledAhead));
        await context.AddMessagesAsync(serviceProvider, [scheduledHead, MessageFor(BehindTheHead)], cancellationToken);

        // Act
        await processor.ProcessUntilIdleAsync(cancellationToken);
        var beforeTheInstant = RecoveringMessageHandler.CalledWith.ToList();

        // simulate the scheduled instant arriving instead of waiting ten minutes for it
        await context.MakeUnhandledMessagesVisibleAsync(cancellationToken);
        await processor.ProcessUntilIdleAsync(cancellationToken);

        // Assert: scheduling the Head delayed the whole Group, and it went first once its instant arrived
        Assert.Empty(beforeTheInstant);
        Assert.Equal([Head, BehindTheHead], RecoveringMessageHandler.CalledWith);
    }

    private ServiceProvider BuildServiceProvider()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddOutboxServices<TestDbContext>(cfg =>
        {
            cfg.AddHandler<RecoveringMessageHandler, RecoveringMessage>();
            // long enough that a Head which failed stays out of sight for the rest of the test
            cfg.BackoffBase = TimeSpan.FromMinutes(10);
            cfg.BackoffJitter = 0;
        });
        serviceCollection.AddBaseServices(Container, _testOutputHelper);

        return serviceCollection.BuildServiceProvider();
    }

    private static OutboxMessage MessageFor(int id, string groupKey = "default", DateTime? visibleAt = null) =>
        new(Guid.NewGuid(), DateTime.UtcNow, new RecoveringMessage(id), groupKey: groupKey, visibleAt: visibleAt);
}
