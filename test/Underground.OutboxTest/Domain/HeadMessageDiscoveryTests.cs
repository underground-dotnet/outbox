using Microsoft.Extensions.DependencyInjection;

using Underground.Outbox.Configuration;
using Underground.Outbox.Data;
using Underground.Outbox.Domain;
using Underground.OutboxTest.TestHandler;

namespace Underground.OutboxTest.Domain;

/// <summary>
/// A Group offers only its HeadMessage - its oldest Stable message not yet completed - and it offers nothing at all
/// while that HeadMessage is not yet visible. These tests are the ones that tell the correct two-step lookup
/// apart from the naive one that filters by visibility first: the naive query passes every test that
/// does not put a HeadMessage out of sight and then look at what happens to the messages behind it.
///
/// As elsewhere, the instant arriving is simulated by moving the stored value into the past rather than
/// by waiting.
/// </summary>
[Collection("ExampleMessageHandler Collection")]
public class HeadMessageDiscoveryTests : DatabaseTest
{
    private static readonly TimeSpan ScheduledAhead = TimeSpan.FromMinutes(10);

    private const int HeadMessage = 1;
    private const int BehindTheHeadMessage = 2;
    private const int OtherGroupFirst = 3;
    private const int OtherGroupSecond = 4;

    private readonly ITestOutputHelper _testOutputHelper;

    public HeadMessageDiscoveryTests(ITestOutputHelper testOutputHelper) : base(testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;

        RecoveringMessageHandler.CalledWith.Clear();
        RecoveringMessageHandler.FailingIds.Clear();
    }

    [Fact]
    public async Task MessagesBehindAHeadMessageInBackoffAreNotHandledEvenThoughTheyAreVisible()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var serviceProvider = BuildServiceProvider();
        var processor = serviceProvider.GetRequiredService<ConcurrentProcessor<OutboxMessage>>();
        var context = CreateDbContext();
        RecoveringMessageHandler.FailingIds.Add(HeadMessage);
        await context.AddMessagesAsync(serviceProvider, [MessageFor(HeadMessage), MessageFor(BehindTheHeadMessage)], cancellationToken);

        // Act: the HeadMessage fails and goes into backoff, and every further run finds it still invisible
        await processor.ProcessUntilIdleAsync(cancellationToken);
        await processor.ProcessUntilIdleAsync(cancellationToken);
        await processor.ProcessUntilIdleAsync(cancellationToken);

        // Assert: the message behind the HeadMessage has been visible throughout and was still never offered
        Assert.Equal([HeadMessage], RecoveringMessageHandler.CalledWith);
    }

    [Fact]
    public async Task OtherGroupsAreHandledWhileOneGroupsHeadMessageIsInBackoff()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var serviceProvider = BuildServiceProvider();
        var processor = serviceProvider.GetRequiredService<ConcurrentProcessor<OutboxMessage>>();
        var context = CreateDbContext();
        RecoveringMessageHandler.FailingIds.Add(HeadMessage);
        await context.AddMessagesAsync(
            serviceProvider,
            [
                MessageFor(HeadMessage, "stalled"),
                MessageFor(BehindTheHeadMessage, "stalled"),
                MessageFor(OtherGroupFirst, "healthy"),
                MessageFor(OtherGroupSecond, "healthy"),
            ],
            cancellationToken);

        // Act: the second run finds the stalled Group's HeadMessage still invisible and the healthy one empty
        await processor.ProcessUntilIdleAsync(cancellationToken);
        await processor.ProcessUntilIdleAsync(cancellationToken);

        // Assert: the healthy Group ran to completion ...
        Assert.Equal(
            [OtherGroupFirst, OtherGroupSecond],
            RecoveringMessageHandler.CalledWith.Where(id => id is OtherGroupFirst or OtherGroupSecond));
        // ... while the stalled Group offered its HeadMessage once and never the message behind it
        Assert.Equal(
            [HeadMessage],
            RecoveringMessageHandler.CalledWith.Where(id => id is HeadMessage or BehindTheHeadMessage));
    }

    [Fact]
    public async Task HeadMessageIsHandledBeforeTheMessagesBehindItOnceItsBackoffHasElapsed()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var serviceProvider = BuildServiceProvider();
        var processor = serviceProvider.GetRequiredService<ConcurrentProcessor<OutboxMessage>>();
        var context = CreateDbContext();
        RecoveringMessageHandler.FailingIds.Add(HeadMessage);
        await context.AddMessagesAsync(serviceProvider, [MessageFor(HeadMessage), MessageFor(BehindTheHeadMessage)], cancellationToken);

        await processor.ProcessUntilIdleAsync(cancellationToken);
        // a further run while the HeadMessage is in backoff must not reach the message behind it either
        await processor.ProcessUntilIdleAsync(cancellationToken);
        var whileInBackoff = RecoveringMessageHandler.CalledWith.ToList();

        // Act: the partner system recovers and the backoff elapses
        RecoveringMessageHandler.FailingIds.Clear();
        await context.MakeIncompleteMessagesVisibleAsync(cancellationToken);
        await processor.ProcessUntilIdleAsync(cancellationToken);

        // Assert
        Assert.Equal([HeadMessage], whileInBackoff);
        Assert.Equal([HeadMessage, HeadMessage, BehindTheHeadMessage], RecoveringMessageHandler.CalledWith);
    }

    [Fact]
    public async Task MessagesBehindAScheduledHeadMessageAreNotHandledUntilThatHeadMessageHasBeen()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var serviceProvider = BuildServiceProvider();
        var processor = serviceProvider.GetRequiredService<ConcurrentProcessor<OutboxMessage>>();
        var context = CreateDbContext();
        var scheduledHeadMessage = MessageFor(HeadMessage, visibleAt: DateTime.UtcNow.Add(ScheduledAhead));
        await context.AddMessagesAsync(serviceProvider, [scheduledHeadMessage, MessageFor(BehindTheHeadMessage)], cancellationToken);

        // Act
        await processor.ProcessUntilIdleAsync(cancellationToken);
        var beforeTheInstant = RecoveringMessageHandler.CalledWith.ToList();

        // simulate the scheduled instant arriving instead of waiting ten minutes for it
        await context.MakeIncompleteMessagesVisibleAsync(cancellationToken);
        await processor.ProcessUntilIdleAsync(cancellationToken);

        // Assert: scheduling the HeadMessage delayed the whole Group, and it went first once its instant arrived
        Assert.Empty(beforeTheInstant);
        Assert.Equal([HeadMessage, BehindTheHeadMessage], RecoveringMessageHandler.CalledWith);
    }

    private ServiceProvider BuildServiceProvider()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddOutboxServices<TestDbContext>(cfg =>
        {
            cfg.AddHandler<RecoveringMessageHandler, RecoveringMessage>();
            // long enough that a HeadMessage which failed stays out of sight for the rest of the test
            cfg.BackoffBase = TimeSpan.FromMinutes(10);
            cfg.BackoffJitter = 0;
        });
        serviceCollection.AddBaseServices(Database, _testOutputHelper);

        return serviceCollection.BuildServiceProvider();
    }

    private static OutboxMessage MessageFor(int id, string groupKey = "default", DateTime? visibleAt = null) =>
        new(Guid.NewGuid(), DateTime.UtcNow, new RecoveringMessage(id), groupKey: groupKey, visibleAt: visibleAt);
}
