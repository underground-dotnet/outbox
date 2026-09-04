using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Underground.Outbox;
using Underground.Outbox.Configuration;
using Underground.Outbox.Data;
using Underground.Outbox.Domain;
using Underground.OutboxTest.TestHandler;

namespace Underground.OutboxTest.Domain;

/// <summary>
/// A caller may say "do not handle this before nine tomorrow morning" when creating a message, instead
/// of building a scheduler alongside the library. As in <see cref="VisibleAtTests"/>, the instant
/// arriving is simulated by moving the stored value into the past rather than by waiting.
/// </summary>
[Collection("ExampleMessageHandler Collection")]
public class ScheduledDeliveryTests : DatabaseTest
{
    private static readonly TimeSpan ScheduledAhead = TimeSpan.FromMinutes(10);

    private readonly ITestOutputHelper _testOutputHelper;

    public ScheduledDeliveryTests(ITestOutputHelper testOutputHelper) : base(testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;

        ExampleMessageHandler.CalledWith.Clear();
        ExampleMessageHandler.ObjectIds.Clear();
    }

    [Fact]
    public async Task ScheduledInstantIsStoredAsSuppliedRatherThanDefaultedToThePresent()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var serviceProvider = BuildServiceProvider();
        var context = CreateDbContext();
        var message = new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new ExampleMessage(10), visibleAt: DateTime.UtcNow.Add(ScheduledAhead));

        // Act
        await AddMessageAsync(serviceProvider, context, message, cancellationToken);

        // Assert: measured against the database's own clock, so a mistranslated instant would show up here
        var secondsUntilVisible = await context.SecondsUntilVisibleAsync(message.Id, cancellationToken);
        Assert.InRange(secondsUntilVisible, ScheduledAhead.TotalSeconds - 60, ScheduledAhead.TotalSeconds);
    }

    [Fact]
    public async Task ScheduledMessageIsNotHandledBeforeItsInstantAndIsHandledAfterIt()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var serviceProvider = BuildServiceProvider();
        var processor = serviceProvider.GetRequiredService<ConcurrentProcessor<OutboxMessage>>();
        var context = CreateDbContext();
        var message = new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new ExampleMessage(10), visibleAt: DateTime.UtcNow.Add(ScheduledAhead));
        await AddMessageAsync(serviceProvider, context, message, cancellationToken);

        // Act
        await processor.ProcessUntilIdleAsync(cancellationToken);
        var beforeTheInstant = ExampleMessageHandler.CalledWith.Count;

        // simulate the scheduled instant arriving instead of waiting ten minutes for it
        await context.MakeUnhandledMessagesVisibleAsync(cancellationToken);
        await processor.ProcessUntilIdleAsync(cancellationToken);

        // Assert
        Assert.Equal(0, beforeTheInstant);
        Assert.Equal(10, Assert.Single(ExampleMessageHandler.CalledWith).Id);
    }

    [Fact]
    public async Task ScheduledMessageDoesNotDelayOtherGroups()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var serviceProvider = BuildServiceProvider();
        var processor = serviceProvider.GetRequiredService<ConcurrentProcessor<OutboxMessage>>();
        var context = CreateDbContext();
        var scheduled = new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new ExampleMessage(1), groupKey: "scheduled", visibleAt: DateTime.UtcNow.Add(ScheduledAhead));
        var immediate = new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new ExampleMessage(2), groupKey: "immediate");
        await AddMessagesAsync(serviceProvider, context, [scheduled, immediate], cancellationToken);

        // Act
        await processor.ProcessUntilIdleAsync(cancellationToken);

        // Assert: the other Group ran to completion while the scheduled one waits
        Assert.Equal(2, Assert.Single(ExampleMessageHandler.CalledWith).Id);
    }

    private ServiceProvider BuildServiceProvider()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddOutboxServices<TestDbContext>(cfg => cfg.AddHandler<ExampleMessageHandler, ExampleMessage>());
        serviceCollection.AddBaseServices(Container, _testOutputHelper);

        return serviceCollection.BuildServiceProvider();
    }

    private static async Task AddMessageAsync(IServiceProvider serviceProvider, TestDbContext context, OutboxMessage message, CancellationToken cancellationToken)
    {
        await AddMessagesAsync(serviceProvider, context, [message], cancellationToken);
    }

    private static async Task AddMessagesAsync(IServiceProvider serviceProvider, TestDbContext context, IEnumerable<OutboxMessage> messages, CancellationToken cancellationToken)
    {
        var outbox = serviceProvider.GetRequiredService<IOutbox>();

        var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await using (transaction.ConfigureAwait(false))
        {
            await outbox.AddMessagesAsync(context, messages, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
    }
}
