using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Underground.Outbox;
using Underground.Outbox.Configuration;
using Underground.Outbox.Data;
using Underground.Outbox.Domain;
using Underground.OutboxTest.TestHandler;

namespace Underground.OutboxTest.Domain;

[Collection("ExampleMessageHandler Collection")]
public class ProcessorTests : DatabaseTest
{
    private readonly ITestOutputHelper _testOutputHelper;

    private readonly IServiceProvider _serviceProvider;

    public ProcessorTests(ITestOutputHelper testOutputHelper) : base(testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;

        // clear the static lists to avoid interference between tests
        ExampleMessageHandler.CalledWith.Clear();
        ExampleMessageHandler.ObjectIds.Clear();

        // setup dependency injection
        var serviceCollection = new ServiceCollection();

        serviceCollection.AddOutboxServices<TestDbContext>(cfg =>
        {
            cfg.AddHandler<ExampleMessageHandler, ExampleMessage>();
            cfg.AddHandler<MultipleMessagesHandler, MultiMessageA>();
            cfg.AddHandler<MultipleMessagesHandler, MultiMessageB>();
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
        await RunBackgroundServiceAsync(TestContext.Current.CancellationToken);

        // Assert
        // due to a race condition with starting the BackgroundService, we need to wait for the handler to be called
        SpinWait.SpinUntil(() => ExampleMessageHandler.CalledWith.Count > 0, TimeSpan.FromSeconds(10));
        Assert.Single(ExampleMessageHandler.CalledWith);
        await StopBackgroundServiceAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Processor_Supports_Cancellation_Token()
    {
        // Arrange
        var context = CreateDbContext();
        var processor = _serviceProvider.GetRequiredService<ConcurrentProcessor<OutboxMessage>>();
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
        // cancel before any message is handled
        await cts.CancelAsync();

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await processor.ProcessUntilIdleAsync(cts.Token));
        Assert.Empty(ExampleMessageHandler.CalledWith);
    }

    [Fact]
    public async Task Processor_With_MultipleMessages()
    {
        // Arrange
        var context = CreateDbContext();
        var msg1 = new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new MultiMessageA(10));
        var msg2 = new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new MultiMessageB(20));
        var outbox = _serviceProvider.GetRequiredService<IOutbox>();
        var processor = _serviceProvider.GetRequiredService<ConcurrentProcessor<OutboxMessage>>();

        await using (var transaction = await context.Database.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            await outbox.AddMessageAsync(context, msg1, TestContext.Current.CancellationToken);
            await outbox.AddMessageAsync(context, msg2, TestContext.Current.CancellationToken);
            await transaction.CommitAsync(TestContext.Current.CancellationToken);
        }

        // Act
        await processor.ProcessUntilIdleAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(MultipleMessagesHandler.CalledWithA);
        Assert.Single(MultipleMessagesHandler.CalledWithB);
    }
}
