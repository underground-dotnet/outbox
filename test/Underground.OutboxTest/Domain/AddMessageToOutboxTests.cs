using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Underground.Outbox;
using Underground.Outbox.Configuration;
using Underground.Outbox.Data;
using Underground.OutboxTest.TestHandler;

namespace Underground.OutboxTest.Domain;

[Collection("AddMessageToOutboxTests Collection")]
public class AddMessageToOutboxTests : DatabaseTest
{
    private readonly IServiceProvider _serviceProvider;

    public AddMessageToOutboxTests(ITestOutputHelper testOutputHelper) : base(testOutputHelper)
    {
        // setup dependency injection
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddBaseServices(Database, testOutputHelper);

        serviceCollection.AddOutboxServices<TestDbContext>(cfg => { });

        _serviceProvider = serviceCollection.BuildServiceProvider();
    }

    [Fact]
    public async Task AddMessageToOutbox_DuplicateEventId_ThrowsException()
    {
        // Arrange
        var context = CreateDbContext();
        var outbox = _serviceProvider.GetRequiredService<IOutbox>();
        var eventId = Guid.NewGuid();
        var msg1 = new OutboxMessage(eventId, DateTime.UtcNow, new ExampleMessage(1));
        var msg2 = new OutboxMessage(eventId, DateTime.UtcNow, new ExampleMessage(2));

        // Act & Assert
        await Assert.ThrowsAsync<DbUpdateException>(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
            await outbox.AddMessageAsync(context, msg1, TestContext.Current.CancellationToken);
            await outbox.AddMessageAsync(context, msg2, TestContext.Current.CancellationToken);
        });
    }

    [Fact]
    public async Task StageMessage_IsWrittenByTheCallersSave_InsideACallerBegunTransaction()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var context = CreateDbContext();
        var outbox = _serviceProvider.GetRequiredService<IOutbox>();
        var message = new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new ExampleMessage(1));

        // Act
        await using (var transaction = await context.Database.BeginTransactionAsync(cancellationToken))
        {
            outbox.StageMessage(context, message);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        // Assert
        await using var assertContext = CreateDbContext();
        Assert.Single(await assertContext.OutboxMessages.Where(m => m.EventId == message.EventId).ToListAsync(cancellationToken));
    }

    [Fact]
    public async Task StageMessage_IsWrittenByTheCallersSave_WithoutATransaction()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var context = CreateDbContext();
        var outbox = _serviceProvider.GetRequiredService<IOutbox>();
        var message = new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new ExampleMessage(2));

        // Act
        outbox.StageMessage(context, message);
        await context.SaveChangesAsync(cancellationToken);

        // Assert
        await using var assertContext = CreateDbContext();
        Assert.Single(await assertContext.OutboxMessages.Where(m => m.EventId == message.EventId).ToListAsync(cancellationToken));
    }
}
