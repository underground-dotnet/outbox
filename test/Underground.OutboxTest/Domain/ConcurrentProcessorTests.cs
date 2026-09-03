using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Underground.Outbox;
using Underground.Outbox.Configuration;
using Underground.Outbox.Data;
using Underground.Outbox.Domain;
using Underground.OutboxTest.TestHandler;

namespace Underground.OutboxTest.Domain;

[Collection("ExampleMessageHandler Collection")]
public class ConcurrentProcessorTests : DatabaseTest
{
    private readonly ITestOutputHelper _testOutputHelper;

    private readonly IServiceProvider _serviceProvider;

    public ConcurrentProcessorTests(ITestOutputHelper testOutputHelper) : base(testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;

        // clear the static lists to avoid interference between tests
        GroupedMessageHandler.CalledWith.Clear();

        // setup dependency injection
        var serviceCollection = new ServiceCollection();

        serviceCollection.AddOutboxServices<TestDbContext>(cfg =>
        {
            cfg.MaxConcurrentGroups = 4;
            cfg.AddHandler<GroupedMessageHandler, GroupedMessage>();
        });

        serviceCollection.AddBaseServices(Container, _testOutputHelper);
        _serviceProvider = serviceCollection.BuildServiceProvider();
    }

    private async Task RunBackgroundServiceAsync(CancellationToken cancellationToken)
    {
        var services = _serviceProvider.GetRequiredService<IEnumerable<IHostedService>>();
        foreach (var service in services)
        {
            await service.StartAsync(cancellationToken);
        }
    }

    private async Task StopBackgroundServiceAsync(CancellationToken cancellationToken)
    {
        var services = _serviceProvider.GetRequiredService<IEnumerable<IHostedService>>();
        foreach (var service in services)
        {
            await service.StopAsync(cancellationToken);
        }
    }

    [Fact]
    public async Task DistributeGroupsAcrossWorkersEqually()
    {
        // Arrange
        var context = CreateDbContext();
        var outbox = _serviceProvider.GetRequiredService<IOutbox>();

        var groups = new[] { "A", "B", "C", "D" };
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
        await RunBackgroundServiceAsync(TestContext.Current.CancellationToken);

        // Assert
        SpinWait.SpinUntil(() => GroupedMessageHandler.TotalCount == 200, TimeSpan.FromSeconds(5));
        await StopBackgroundServiceAsync(TestContext.Current.CancellationToken);
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
        using var cts = new CancellationTokenSource();
        var processor = _serviceProvider.GetRequiredService<ConcurrentProcessor<OutboxMessage>>();
        _ = processor.StartAsync(cts.Token);
        processor.ScheduleProcessingRun();
        processor.ScheduleProcessingRun();
        processor.ScheduleProcessingRun();

        // Assert
        SpinWait.SpinUntil(() => GroupedMessageHandler.TotalCount == 200, TimeSpan.FromSeconds(5));
        await cts.CancelAsync();
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
}
