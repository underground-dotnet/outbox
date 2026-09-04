using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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

        serviceCollection.AddOutboxServices<TestDbContext>(cfg =>
            cfg.AddHandler<ExampleMessageHandler, ExampleMessage>(ServiceLifetime.Scoped));

        serviceCollection.AddBaseServices(Container, _testOutputHelper);
        _serviceProvider = serviceCollection.BuildServiceProvider();
    }

    [Fact]
    public async Task ProcessGroupsInSeparateScopes()
    {
        // Arrange
        var context = CreateDbContext();
        var msg1 = new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new ExampleMessage(10)) { GroupKey = "A" };
        var msg2 = new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new ExampleMessage(11)) { GroupKey = "B" };
        var outbox = _serviceProvider.GetRequiredService<IOutbox>();
        var processor = _serviceProvider.GetRequiredService<ConcurrentProcessor<OutboxMessage>>();

        // Act
        await using (var transaction = await context.Database.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            await outbox.AddMessageAsync(context, msg1, TestContext.Current.CancellationToken);
            await outbox.AddMessageAsync(context, msg2, TestContext.Current.CancellationToken);
            await transaction.CommitAsync(TestContext.Current.CancellationToken);
        }
        await processor.ProcessUntilIdleAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, ExampleMessageHandler.ObjectIds.Count);
    }

    [Fact]
    public async Task ProcessingUsesANewScopeForEachMessageOfAGroup()
    {
        // Arrange
        var context = CreateDbContext();
        var msg1 = new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new ExampleMessage(10)) { GroupKey = "A" };
        var msg2 = new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new ExampleMessage(11)) { GroupKey = "A" };
        var msg3 = new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new ExampleMessage(12)) { GroupKey = "A" };
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
        await processor.ProcessUntilIdleAsync(TestContext.Current.CancellationToken);

        // Assert: a Group offers one message per claim, and every claim takes a scope of its own
        Assert.Equal(3, ExampleMessageHandler.CalledWith.Count);
        Assert.Equal(3, ExampleMessageHandler.ObjectIds.Count);
    }

    [Fact]
    public async Task KeepProcessingUntilOutboxIsEmpty()
    {
        // Arrange
        var context = CreateDbContext();
        var msg1 = new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new ExampleMessage(10)) { GroupKey = "A" };
        var msg2 = new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new ExampleMessage(11)) { GroupKey = "A" };
        var msg3 = new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new ExampleMessage(12)) { GroupKey = "A" };
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
        await processor.ProcessUntilIdleAsync(TestContext.Current.CancellationToken);

        // Assert
        var completed = await context.Database
            .SqlQuery<int>($"SELECT COUNT(id) AS \"Value\" FROM public.outbox WHERE processed_at IS NULL")
            .SingleAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(0, completed);
    }
}
