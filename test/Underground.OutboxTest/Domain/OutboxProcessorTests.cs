using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Underground.Outbox;
using Underground.Outbox.Configuration;
using Underground.Outbox.Data;

namespace Underground.OutboxTest.Domain;

public class OutboxProcessorTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;

    private readonly IServiceProvider _serviceProvider;

    public OutboxProcessorTests(DatabaseFixture fixture)
    {
        _fixture = fixture;

        // clear the static lists to avoid interference between tests
        ExampleMessageHandler.CalledWith.Clear();

        // setup dependency injection
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddBaseServices(fixture.Container, fixture.MessageSink);

        serviceCollection.AddOutboxServices(cfg =>
        {
            cfg.UseDbContext<TestDbContext>();
            cfg.AddHandler<ExampleMessageHandler>();
            /*cfg.OutboxType = typeof(InMemoryOutbox)*/
        });

        _serviceProvider = serviceCollection.BuildServiceProvider();

        // clear the outbox tables before each test
        _fixture.CreateDbContext().Database.ExecuteSqlRaw("TRUNCATE TABLE \"outbox\"");
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
        var context = _fixture.CreateDbContext();
        var msg = new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new ExampleMessage(10));
        var outbox = _serviceProvider.GetRequiredService<IOutbox>();

        // Act
        await using var transaction = await context.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
        await outbox.AddMessageAsync(context, msg);
        await transaction.CommitAsync(TestContext.Current.CancellationToken);
        await RunBackgroundServiceAsync();

        // Assert
        // due to a race condition with starting the BackgroundService, we need to wait for the handler to be called
        SpinWait.SpinUntil(() => ExampleMessageHandler.CalledWith.Count > 0, TimeSpan.FromSeconds(3));
        Assert.Single(ExampleMessageHandler.CalledWith);
        Assert.Empty(ExampleMessageAnotherHandler.CalledWith);
        await StopBackgroundServiceAsync();
    }
}
