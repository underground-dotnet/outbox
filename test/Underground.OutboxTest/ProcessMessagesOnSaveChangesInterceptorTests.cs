using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Underground.Outbox;
using Underground.Outbox.Configuration;
using Underground.Outbox.Data;
using Underground.Outbox.Domain;
using Underground.OutboxTest.TestHandler;

namespace Underground.OutboxTest;

public class ProcessMessagesOnSaveChangesInterceptorTests : DatabaseTest
{
    private readonly ITestOutputHelper _testOutputHelper;
    private readonly IServiceProvider _serviceProvider;

    public ProcessMessagesOnSaveChangesInterceptorTests(ITestOutputHelper testOutputHelper) : base(testOutputHelper)
    {
        Container.StartAsync(CancellationToken.None).GetAwaiter().GetResult();

        _testOutputHelper = testOutputHelper;
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddBaseServices(Container, _testOutputHelper);

        serviceCollection.AddOutboxServices<TestDbContext>(cfg =>
        {
            cfg.AddHandler<ExampleMessageHandler, ExampleMessage>();
        });

        serviceCollection.AddScoped<ConcurrentProcessor<OutboxMessage>, NoPollingProcessor<OutboxMessage>>();

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
    public async Task SaveChanges_TriggersOutboxProcessing_WhenNewOutboxMessagesWereAdded()
    {
        // Arrange
        var context = CreateDbContext(_serviceProvider.GetRequiredService<ProcessMessagesOnSaveChangesInterceptor>());
        var outbox = _serviceProvider.GetRequiredService<IOutbox>();
        var msg1 = new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new ExampleMessage(10));
        await RunBackgroundServiceAsync(TestContext.Current.CancellationToken);

        // Act
        await using (var transaction = await context.Database.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            await outbox.AddMessageAsync(context, msg1, TestContext.Current.CancellationToken);
            await transaction.CommitAsync(TestContext.Current.CancellationToken);
        }

        // Assert
        SpinWait.SpinUntil(() => ExampleMessageHandler.CalledWith.Count > 0, TimeSpan.FromSeconds(3));
        Assert.Single(ExampleMessageHandler.ObjectIds);
        await StopBackgroundServiceAsync(TestContext.Current.CancellationToken);
    }
}
