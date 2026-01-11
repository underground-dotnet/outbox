using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Underground.Outbox;
using Underground.Outbox.Configuration;
using Underground.Outbox.Data;
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
        PartitionedMessageHandler.CalledWith.Clear();

        // setup dependency injection
        var serviceCollection = new ServiceCollection();

        serviceCollection.AddOutboxServices<TestDbContext>(cfg =>
        {
            cfg.AddHandler<PartitionedMessageHandler>();
        });

        serviceCollection.AddBaseServices(Container, _testOutputHelper);
        _serviceProvider = serviceCollection.BuildServiceProvider();
    }

    private async Task RunBackgroundServiceAsync()
    {
        var service = _serviceProvider.GetRequiredService<IHostedService>();
        await service.StartAsync(CancellationToken.None);
    }

    private async Task StopBackgroundServiceAsync()
    {
        var service = _serviceProvider.GetRequiredService<IHostedService>();
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task DistributePartitionsAcrossWorkersEqually()
    {
        // Arrange
        var context = CreateDbContext();
        var outbox = _serviceProvider.GetRequiredService<IOutbox>();

        var partitions = new[] { "A", "B", "C", "D" };
        await using (var transaction = await context.Database.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            for (int i = 0; i < 200; i++)
            {
                var partition = partitions[i % partitions.Length];
                var msg = new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new ExampleMessage(i)) { PartitionKey = partition };
                await outbox.AddMessageAsync(context, msg, TestContext.Current.CancellationToken);
            }

            await transaction.CommitAsync(TestContext.Current.CancellationToken);
        }

        // Act
        await RunBackgroundServiceAsync();

        // Assert
        SpinWait.SpinUntil(() => PartitionedMessageHandler.TotalCount == 200, TimeSpan.FromSeconds(5));
        await StopBackgroundServiceAsync();
        Assert.Equal(200, PartitionedMessageHandler.TotalCount);

        var partitionA = Enumerable.Range(0, 200)
                     .Where(n => n % 4 == 0)
                     .ToList();
        Assert.Equal(partitionA, PartitionedMessageHandler.CalledWith["A"]);

        var partitionB = Enumerable.Range(0, 200)
                     .Where(n => n % 4 == 1)
                     .ToList();
        Assert.Equal(partitionB, PartitionedMessageHandler.CalledWith["B"]);

        var partitionC = Enumerable.Range(0, 200)
                             .Where(n => n % 4 == 2)
                             .ToList();
        Assert.Equal(partitionC, PartitionedMessageHandler.CalledWith["C"]);

        var partitionD = Enumerable.Range(0, 200)
                     .Where(n => n % 4 == 3)
                     .ToList();
        Assert.Equal(partitionD, PartitionedMessageHandler.CalledWith["D"]);
    }
}
