using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Underground.Outbox;
using Underground.Outbox.Configuration;
using Underground.Outbox.Data;
using Underground.OutboxTest.TestHandler;

namespace Underground.OutboxTest.Domain;

[Collection("ExampleMessageHandler Collection")]
public class ProcessorTests : DatabaseTest
{
    private readonly ITestOutputHelper _testOutputHelper;

    private readonly IServiceProvider _serviceProvider;

    public ProcessorTests(ITestOutputHelper testOutputHelper) : base(testOutputHelper)
    {
        Container.StartAsync(CancellationToken.None).GetAwaiter().GetResult();

        _testOutputHelper = testOutputHelper;

        // clear the static lists to avoid interference between tests
        ExampleMessageHandler.CalledWith.Clear();
        ExampleMessageHandler.ObjectIds.Clear();

        // setup dependency injection
        var serviceCollection = new ServiceCollection();

        serviceCollection.AddOutboxServices<TestDbContext>(cfg =>
        {
            cfg.AddHandler<ExampleMessageHandler>();
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
    public async Task SendIntegrationEventFromOutbox()
    {
        // Arrange
        var context = CreateDbContext();
        var msg = new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new ExampleMessage(10));
        var outbox = _serviceProvider.GetRequiredService<IOutbox>();

        // Act
        await using var transaction = await context.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
        await outbox.AddMessageAsync(context, msg, TestContext.Current.CancellationToken);
        await transaction.CommitAsync(TestContext.Current.CancellationToken);
        await RunBackgroundServiceAsync();

        // Assert
        // due to a race condition with starting the BackgroundService, we need to wait for the handler to be called
        SpinWait.SpinUntil(() => ExampleMessageHandler.CalledWith.Count > 0, TimeSpan.FromSeconds(3));
        Assert.Single(ExampleMessageHandler.CalledWith);
        Assert.Empty(ExampleMessageAnotherHandler.CalledWith);
        await StopBackgroundServiceAsync();
    }

    [Fact]
    public async Task Processor_Supports_Cancellation_Token()
    {
        // Arrange
        var context = CreateDbContext();
        var processor = _serviceProvider.GetRequiredService<SynchronousProcessor<OutboxMessage>>();
        var outbox = _serviceProvider.GetRequiredService<IOutbox>();

        await using (var transaction = await context.Database.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            foreach (var i in Enumerable.Range(1, 100))
            {
                var msg = new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new ExampleMessage(i));
                await outbox.AddMessageAsync(context, msg, TestContext.Current.CancellationToken);
            }

            await transaction.CommitAsync(TestContext.Current.CancellationToken);
        }

        // Act
        using var cts = new CancellationTokenSource();
        var task = processor.StartAsync(cts.Token);
        // cancel processing early
        await cts.CancelAsync();

        // Assert
        await Assert.ThrowsAsync<TaskCanceledException>(async () => await task);
        Assert.True(ExampleMessageHandler.CalledWith.Count < 100);
    }
}
