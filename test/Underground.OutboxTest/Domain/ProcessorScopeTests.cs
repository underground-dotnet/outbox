using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Underground.Outbox;
using Underground.Outbox.Configuration;
using Underground.Outbox.Data;
using Underground.Outbox.Domain;
using Underground.OutboxTest.TestHandler;

namespace Underground.OutboxTest.Domain;

[Collection("ExampleMessageHandler Collection")]
public class ProcessorScopeTests : DatabaseTest
{
    private readonly ITestOutputHelper _testOutputHelper;

    private readonly IServiceProvider _serviceProvider;

    public ProcessorScopeTests(ITestOutputHelper testOutputHelper) : base(testOutputHelper)
    {
        Container.StartAsync(CancellationToken.None).GetAwaiter().GetResult();

        _testOutputHelper = testOutputHelper;

        // clear the static lists to avoid interference between tests
        ExampleMessageHandler.CalledWith.Clear();
        ExampleMessageHandler.ObjectIds.Clear();

        // setup dependency injection
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddBaseServices(Container, _testOutputHelper);

        serviceCollection.AddOutboxServices<TestDbContext>(cfg =>
        {
            cfg.AddHandler<ExampleMessageHandler>(ServiceLifetime.Scoped);
            cfg.BatchSize = 2;
        });

        _serviceProvider = serviceCollection.BuildServiceProvider();
    }

    [Fact]
    public async Task ProcessPartitionsInSeparateScopes()
    {
        // Arrange
        var context = CreateDbContext();
        var msg1 = new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new ExampleMessage(10)) { PartitionKey = "A" };
        var msg2 = new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new ExampleMessage(11)) { PartitionKey = "B" };
        var outbox = _serviceProvider.GetRequiredService<IOutbox>();
        var processor = _serviceProvider.GetRequiredService<ConcurrentProcessor<OutboxMessage>>();

        // Act
        await using (var transaction = await context.Database.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            await outbox.AddMessageAsync(context, msg1, TestContext.Current.CancellationToken);
            await outbox.AddMessageAsync(context, msg2, TestContext.Current.CancellationToken);
            await transaction.CommitAsync(TestContext.Current.CancellationToken);
        }
        await processor.ProcessAndWaitAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, ExampleMessageHandler.ObjectIds.Count);
    }

    [Fact]
    public async Task ProcessingInsidePartitionBatchUsesSameScope()
    {
        // Arrange
        var context = CreateDbContext();
        var msg1 = new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new ExampleMessage(10)) { PartitionKey = "A" };
        var msg2 = new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new ExampleMessage(11)) { PartitionKey = "A" };
        var outbox = _serviceProvider.GetRequiredService<IOutbox>();
        var processor = _serviceProvider.GetRequiredService<ConcurrentProcessor<OutboxMessage>>();

        // Act
        await using (var transaction = await context.Database.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            await outbox.AddMessageAsync(context, msg1, TestContext.Current.CancellationToken);
            await outbox.AddMessageAsync(context, msg2, TestContext.Current.CancellationToken);
            await transaction.CommitAsync(TestContext.Current.CancellationToken);
        }
        await processor.ProcessAndWaitAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(ExampleMessageHandler.ObjectIds);
    }

    [Fact]
    public async Task ProcessingUsesNewScopeForEachBatch()
    {
        // Arrange
        var context = CreateDbContext();
        var msg1 = new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new ExampleMessage(10)) { PartitionKey = "A" };
        var msg2 = new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new ExampleMessage(11)) { PartitionKey = "A" };
        var msg3 = new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new ExampleMessage(12)) { PartitionKey = "A" };
        var outbox = _serviceProvider.GetRequiredService<IOutbox>();
        var processor = _serviceProvider.GetRequiredService<ConcurrentProcessor<OutboxMessage>>();
        var logger = _serviceProvider.GetRequiredService<ILogger<ProcessorScopeTests>>();

        // Act
        await using (var transaction = await context.Database.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            await outbox.AddMessageAsync(context, msg1, TestContext.Current.CancellationToken);
            await outbox.AddMessageAsync(context, msg2, TestContext.Current.CancellationToken);
            await outbox.AddMessageAsync(context, msg3, TestContext.Current.CancellationToken);
            await transaction.CommitAsync(TestContext.Current.CancellationToken);
        }
        await processor.ProcessAndWaitAsync(TestContext.Current.CancellationToken);
        logger.LogInformation("Test finished");

        // Assert
        Assert.Equal(3, ExampleMessageHandler.CalledWith.Count);
        Assert.Equal(2, ExampleMessageHandler.ObjectIds.Count);
    }

    [Fact]
    public async Task KeepProcessingUntilOutboxIsEmpty()
    {
        // Arrange
        var context = CreateDbContext();
        var msg1 = new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new ExampleMessage(10)) { PartitionKey = "A" };
        var msg2 = new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new ExampleMessage(11)) { PartitionKey = "A" };
        var msg3 = new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new ExampleMessage(12)) { PartitionKey = "A" };
        var outbox = _serviceProvider.GetRequiredService<IOutbox>();
        var processor = _serviceProvider.GetRequiredService<ConcurrentProcessor<OutboxMessage>>();

        // Act
        await using (var transaction = await context.Database.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            await outbox.AddMessageAsync(context, msg1, TestContext.Current.CancellationToken);
            await outbox.AddMessageAsync(context, msg2, TestContext.Current.CancellationToken);
            await outbox.AddMessageAsync(context, msg3, TestContext.Current.CancellationToken);
            await transaction.CommitAsync(TestContext.Current.CancellationToken);
        }
        // Batch 1
        await processor.ProcessAndWaitAsync(TestContext.Current.CancellationToken);

        // Assert
        var completed = await context.Database
            .SqlQuery<int>($"SELECT COUNT(id) AS \"Value\" FROM public.outbox WHERE processed_at IS NULL")
            .SingleAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(0, completed);
    }
}
