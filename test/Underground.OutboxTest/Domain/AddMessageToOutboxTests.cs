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
        Container.StartAsync(CancellationToken.None).GetAwaiter().GetResult();

        // setup dependency injection
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddBaseServices(Container, testOutputHelper);

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
}
