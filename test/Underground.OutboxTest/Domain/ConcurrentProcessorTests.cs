using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Underground.Outbox;
using Underground.Outbox.Configuration;
using Underground.Outbox.Data;
using Underground.Outbox.Domain;
using Underground.OutboxTest.TestHandler;

namespace Underground.OutboxTest.Domain;

/// <summary>
/// Workers serve themselves: each one claims a Head, handles it, and claims again, and nothing hands
/// Groups out to them. These tests are about that topology - that distinct Groups really do end up on
/// different workers, and that one Group holding a worker leaves the others alone.
/// </summary>
[Collection("ExampleMessageHandler Collection")]
public class ConcurrentProcessorTests : DatabaseTest
{
    private readonly ITestOutputHelper _testOutputHelper;

    private readonly IServiceProvider _serviceProvider;

    public ConcurrentProcessorTests(ITestOutputHelper testOutputHelper) : base(testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;

        // clear the static state to avoid interference between tests
        GroupedMessageHandler.CalledWith.Clear();
        GroupedMessageHandler.ExpectedConcurrentGroups = 0;
        GroupedMessageHandler.GroupsHandledConcurrently = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        BlockingMessageHandler.Reset();

        _serviceProvider = BuildServiceProvider(cfg =>
        {
            cfg.MaxConcurrentGroups = 4;
            cfg.AddHandler<GroupedMessageHandler, GroupedMessage>();
        });
    }

    private ServiceProvider BuildServiceProvider(Action<OutboxServiceConfiguration> configure)
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddOutboxServices<TestDbContext>(configure);
        serviceCollection.AddBaseServices(Container, _testOutputHelper);

        return serviceCollection.BuildServiceProvider();
    }

    private static async Task RunBackgroundServiceAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        var services = serviceProvider.GetRequiredService<IEnumerable<IHostedService>>();
        foreach (var service in services)
        {
            await service.StartAsync(cancellationToken);
        }
    }

    private static async Task StopBackgroundServiceAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        var services = serviceProvider.GetRequiredService<IEnumerable<IHostedService>>();
        foreach (var service in services)
        {
            await service.StopAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Polls a condition the background workers bring about, rather than sleeping for a fixed duration.
    /// Returns whether it came about before the timeout, so a test asserts on that instead of hanging.
    /// </summary>
    private static async Task<bool> WaitUntilAsync(Func<bool> condition, CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        while (!condition())
        {
            if (timeout.IsCancellationRequested)
            {
                return false;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20), cancellationToken);
        }

        return true;
    }

    [Fact]
    public async Task DistributeGroupsAcrossWorkersEqually()
    {
        // Arrange
        var context = CreateDbContext();
        var outbox = _serviceProvider.GetRequiredService<IOutbox>();

        var groups = new[] { "A", "B", "C", "D" };
        // no handler returns before all four Groups are in flight, so the test cannot pass on a serial run
        GroupedMessageHandler.ExpectedConcurrentGroups = groups.Length;
        await using (var transaction = await context.Database.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            for (int i = 0; i < 200; i++)
            {
                var groupKey = groups[i % groups.Length];
                var msg = new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new GroupedMessage(i)) { GroupKey = groupKey };
                await outbox.AddMessageAsync(context, msg, TestContext.Current.CancellationToken);
            }

            await transaction.CommitAsync(TestContext.Current.CancellationToken);
        }

        // Act
        await RunBackgroundServiceAsync(_serviceProvider, TestContext.Current.CancellationToken);

        // Assert
        await GroupedMessageHandler.GroupsHandledConcurrently.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        SpinWait.SpinUntil(() => GroupedMessageHandler.TotalCount == 200, TimeSpan.FromSeconds(10));
        await StopBackgroundServiceAsync(_serviceProvider, TestContext.Current.CancellationToken);
        Assert.Equal(200, GroupedMessageHandler.TotalCount);

        var groupA = Enumerable.Range(0, 200)
                     .Where(n => n % 4 == 0)
                     .ToList();
        Assert.Equal(groupA, GroupedMessageHandler.CalledWith["A"]);

        var groupB = Enumerable.Range(0, 200)
                     .Where(n => n % 4 == 1)
                     .ToList();
        Assert.Equal(groupB, GroupedMessageHandler.CalledWith["B"]);

        var groupC = Enumerable.Range(0, 200)
                             .Where(n => n % 4 == 2)
                             .ToList();
        Assert.Equal(groupC, GroupedMessageHandler.CalledWith["C"]);

        var groupD = Enumerable.Range(0, 200)
                     .Where(n => n % 4 == 3)
                     .ToList();
        Assert.Equal(groupD, GroupedMessageHandler.CalledWith["D"]);
    }

    [Fact]
    public async Task DoNotProcessMessagesTwice()
    {
        // Arrange
        var context = CreateDbContext();
        var outbox = _serviceProvider.GetRequiredService<IOutbox>();

        var groups = new[] { "A", "B" };
        await using (var transaction = await context.Database.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            for (int i = 0; i < 200; i++)
            {
                var groupKey = groups[i % groups.Length];
                var msg = new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new GroupedMessage(i)) { GroupKey = groupKey };
                await outbox.AddMessageAsync(context, msg, TestContext.Current.CancellationToken);
            }

            await transaction.CommitAsync(TestContext.Current.CancellationToken);
        }

        // Act
        var processor = _serviceProvider.GetRequiredService<ConcurrentProcessor<OutboxMessage>>();
        await processor.ProcessUntilIdleAsync(TestContext.Current.CancellationToken);
        // a second run must not hand out the messages of the first one again
        await processor.ProcessUntilIdleAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(200, GroupedMessageHandler.TotalCount);

        var groupA = Enumerable.Range(0, 200)
                     .Where(n => n % 4 == 0)
                     .ToList();
        Assert.Equal(groupA, GroupedMessageHandler.CalledWith["A"]);

        var groupB = Enumerable.Range(0, 200)
                     .Where(n => n % 4 == 1)
                     .ToList();
        Assert.Equal(groupB, GroupedMessageHandler.CalledWith["B"]);
    }

    /// <summary>
    /// Two workers, one of them held inside a handler for as long as the test likes. The other has to get
    /// through the whole of the second Group unaided, which it can only do by serving itself: there is no
    /// worker left to be handed anything by.
    /// </summary>
    [Fact]
    public async Task SlowHandlerInOneGroupDoesNotDelayAnotherGroup()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var serviceProvider = BuildServiceProvider(cfg =>
        {
            // exactly one worker is left over once the slow Group has taken one
            cfg.MaxConcurrentGroups = 2;
            cfg.AddHandler<BlockingMessageHandler, BlockingMessage>();
        });
        var context = CreateDbContext();

        const int slow = 1;
        int[] fast = [2, 3, 4];
        BlockingMessageHandler.BlockingIds.Add(slow);
        await context.AddMessagesAsync(
            serviceProvider,
            [
                MessageFor(slow, "slow"),
                .. fast.Select(id => MessageFor(id, "fast")),
            ],
            cancellationToken);

        // Act
        await RunBackgroundServiceAsync(serviceProvider, cancellationToken);
        await BlockingMessageHandler.Blocked.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        var fastGroupFinished = await WaitUntilAsync(
            () => fast.All(BlockingMessageHandler.CalledWith.Contains),
            cancellationToken);

        // Assert: the whole of the fast Group was handled while the slow Group was still inside its handler
        var slowMessageStillUnhandled = await context.OutboxMessages
            .AsNoTracking()
            .AnyAsync(m => m.GroupKey == "slow" && m.ProcessedAt == null, cancellationToken);

        BlockingMessageHandler.Release.TrySetResult();
        await StopBackgroundServiceAsync(serviceProvider, cancellationToken);

        Assert.True(fastGroupFinished, "the fast Group was still waiting when the timeout elapsed");
        Assert.True(slowMessageStillUnhandled, "the slow Group finished before the fast one, so nothing was proven");
        Assert.Equal(fast, BlockingMessageHandler.CalledWith.Where(id => id != slow));
    }

    private static OutboxMessage MessageFor(int id, string groupKey) =>
        new(Guid.NewGuid(), DateTime.UtcNow, new BlockingMessage(id), groupKey: groupKey);
}
