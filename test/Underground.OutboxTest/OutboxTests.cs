using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Underground.Outbox;
using Underground.Outbox.Configuration;
using Underground.Outbox.Data;
using Underground.Outbox.Exceptions;

namespace Underground.OutboxTest;

public class OutboxTests : DatabaseTest
{
    private readonly ITestOutputHelper _testOutputHelper;

    private readonly IServiceProvider _serviceProvider;

    public OutboxTests(ITestOutputHelper testOutputHelper) : base(testOutputHelper)
    {
        Container.StartAsync(CancellationToken.None).GetAwaiter().GetResult();

        _testOutputHelper = testOutputHelper;
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddBaseServices(Container, _testOutputHelper);

        serviceCollection.AddOutboxServices(cfg => cfg.UseDbContext<TestDbContext>());

        _serviceProvider = serviceCollection.BuildServiceProvider();
    }

    [Fact]
    public async Task AddMessage_ThrowsNoActiveTransactionException_WhenThereIsNoActiveTransaction()
    {
        // Arrange
        var context = CreateDbContext();
        var outbox = _serviceProvider.GetRequiredService<IOutbox>();

        // Act & Assert
        await Assert.ThrowsAsync<NoActiveTransactionException>(async () =>
            await outbox.AddMessageAsync(
                context,
                new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new ExampleMessage(1))
            )
        );
    }

    [Fact]
    public async Task AddMessages_ThrowsNoActiveTransactionException_WhenThereIsNoActiveTransaction()
    {
        // Arrange
        var context = CreateDbContext();
        var outbox = _serviceProvider.GetRequiredService<IOutbox>();

        // Act & Assert
        await Assert.ThrowsAsync<NoActiveTransactionException>(async () =>
            await outbox.AddMessagesAsync(
                context,
                [
                    new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new ExampleMessage(1)),
                    new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new ExampleMessage(2))
                ]
            )
        );
    }

    [Fact]
    public async Task AddMessage_AddsOutboxMessageToTransaction()
    {
        // Arrange
        var context = CreateDbContext();
        var outbox = _serviceProvider.GetRequiredService<IOutbox>();

        // Act
        using var transaction = await context.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
        await outbox.AddMessageAsync(
            context,
            new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new ExampleMessage(1))
        );

        // Assert
        Assert.Equal(1, await context.Database.SqlQuery<int>($"SELECT COUNT(*) AS \"Value\" FROM outbox").SingleAsync(cancellationToken: TestContext.Current.CancellationToken));
        await transaction.RollbackAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AddMessages_AddsOutboxMessagesToTransaction()
    {
        // Arrange
        var context = CreateDbContext();
        var outbox = _serviceProvider.GetRequiredService<IOutbox>();

        // Act
        using var transaction = await context.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
        await outbox.AddMessagesAsync(
            context,
            [
                new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new ExampleMessage(1)),
                new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new ExampleMessage(2))
            ]
        );

        // Assert
        Assert.Equal(2, await context.Database.SqlQuery<int>($"SELECT COUNT(*) AS \"Value\" FROM outbox").SingleAsync(cancellationToken: TestContext.Current.CancellationToken));
        await transaction.RollbackAsync(TestContext.Current.CancellationToken);
    }

    // [Fact]
    // public async Task AddMessage_()
    // {
    //     // Arrange
    //     var serviceCollection = new ServiceCollection();
    //     serviceCollection.AddOutboxServices("module1", cfg =>
    //     {
    //         cfg.ConnectionString = _fixture.PostgreSqlContainer.GetConnectionString();
    //         cfg.AddHandler<ExampleMessageHandler>();
    //     });

    //     serviceCollection.AddOutboxServices("module2", cfg =>
    //     {
    //         cfg.ConnectionString = _fixture.PostgreSqlContainer.GetConnectionString();
    //         cfg.AddHandler<ExampleMessageAnotherHandler>();
    //     });
    // }
}
