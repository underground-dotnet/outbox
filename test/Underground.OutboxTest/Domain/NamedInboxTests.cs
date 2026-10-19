using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Underground.Outbox;
using Underground.Outbox.Configuration;
using Underground.Outbox.Data;
using Underground.Outbox.Domain;
using Underground.Outbox.Domain.Dispatchers;
using Underground.Outbox.Domain.Inboxes;
using Underground.Outbox.Exceptions;
using Underground.OutboxTest.TestHandler;

namespace Underground.OutboxTest.Domain;

/// <summary>
/// Several Inboxes in one database, each with its own Handler for one message type. A message is staged into
/// every Inbox that handles it, and what one Inbox does with its copy never decides what another does.
/// </summary>
public class NamedInboxTests : DatabaseTest
{
    private const string Payments = "payments";
    private const string Shipping = "shipping";

    private readonly ITestOutputHelper _testOutputHelper;

    public NamedInboxTests(ITestOutputHelper testOutputHelper) : base(testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
    }

    [Fact]
    public async Task Staging_WritesOneMessageIntoEveryInboxThatHandlesItsType()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateServiceProvider();
        var message = new InboxMessage(Guid.NewGuid(), DateTime.UtcNow, new FannedOutMessage(1), groupKey: "order-1");

        // Act
        await StageAsync(provider, [message], cancellationToken);

        // Assert
        var stored = await ReadMessagesAsync(provider, cancellationToken);

        Assert.Equal([Payments, Shipping], stored.Select(m => m.Inbox).Order(StringComparer.Ordinal), StringComparer.Ordinal);
        Assert.All(stored, m => Assert.Equal(message.EventId, m.EventId));
        Assert.All(stored, m => Assert.Equal("order-1", m.GroupKey));
    }

    [Fact]
    public async Task Staging_Throws_AndStagesNothing_WhenNoInboxHandlesAMessagesType()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<InboxOutboxDbContext>();
        var inbox = scope.ServiceProvider.GetRequiredService<IInbox>();

        InboxMessage[] messages =
        [
            new(Guid.NewGuid(), DateTime.UtcNow, new FannedOutMessage(1)),
            new(Guid.NewGuid(), DateTime.UtcNow, new UnhandledMessage()),
        ];

        // Act
        var exception = Assert.ThrowsAny<InvalidOperationException>(() => inbox.StageMessages(context, messages));

        // Assert
        Assert.Contains(typeof(UnhandledMessage).FullName!, exception.Message, StringComparison.Ordinal);
        Assert.Empty(context.ChangeTracker.Entries<InboxMessage>());
        await context.SaveChangesAsync(cancellationToken);
        Assert.Empty(await ReadMessagesAsync(provider, cancellationToken));
    }

    [Fact]
    public async Task Staging_RejectsAnEventIdItsInboxAlreadyHolds()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateServiceProvider();
        var eventId = Guid.NewGuid();
        await StageAsync(provider, [new InboxMessage(eventId, DateTime.UtcNow, new FannedOutMessage(1))], cancellationToken);

        // Act & Assert
        await Assert.ThrowsAsync<DbUpdateException>(() =>
            StageAsync(provider, [new InboxMessage(eventId, DateTime.UtcNow, new FannedOutMessage(1))], cancellationToken));
    }

    [Fact]
    public async Task AFailingInbox_NeitherRepeatsNorHoldsBackAnotherInbox()
    {
        // Arrange: Shipping fails every message, so its Group stalls behind the first one
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateServiceProvider();

        await StageAsync(
            provider,
            [
                new InboxMessage(Guid.NewGuid(), DateTime.UtcNow, new FannedOutMessage(1), groupKey: "order-1"),
                new InboxMessage(Guid.NewGuid(), DateTime.UtcNow, new FannedOutMessage(2), groupKey: "order-1"),
            ],
            cancellationToken);

        // Act
        await provider.InboxWorkers(Shipping).ProcessUntilIdleAsync(cancellationToken);
        await provider.InboxWorkers(Payments).ProcessUntilIdleAsync(cancellationToken);

        // Assert
        var stored = await ReadMessagesAsync(provider, cancellationToken);

        var payments = stored.Where(m => string.Equals(m.Inbox, Payments, StringComparison.Ordinal)).ToList();
        Assert.All(payments, m => Assert.NotNull(m.CompletedAt));
        Assert.All(payments, m => Assert.Equal(0, m.RetryCount));

        var shipping = stored.Where(m => string.Equals(m.Inbox, Shipping, StringComparison.Ordinal)).OrderBy(m => m.Id).ToList();
        Assert.All(shipping, m => Assert.Null(m.CompletedAt));
        Assert.Equal([1, 0], shipping.Select(m => m.RetryCount));

        // Payments' writes committed once each; Shipping's attempt was rolled back to its savepoint
        await using var scope = provider.CreateAsyncScope();
        var users = await scope.ServiceProvider.GetRequiredService<InboxOutboxDbContext>().Users
            .Select(u => u.Name)
            .ToListAsync(cancellationToken);
        Assert.Equal(["payments-1", "payments-2"], users.Order(StringComparer.Ordinal), StringComparer.Ordinal);
    }

    [Fact]
    public async Task Cleanup_DeletesOnlyItsOwnInboxsCompletedMessages()
    {
        // Arrange: Payments keeps nothing once completed, Shipping keeps the default week
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateServiceProvider(shippingFails: false);
        await StageAsync(provider, [new InboxMessage(Guid.NewGuid(), DateTime.UtcNow, new FannedOutMessage(1))], cancellationToken);
        await provider.InboxWorkers(Payments).ProcessUntilIdleAsync(cancellationToken);
        await provider.InboxWorkers(Shipping).ProcessUntilIdleAsync(cancellationToken);

        // Act
        foreach (var inbox in new[] { Payments, Shipping })
        {
            await using var scope = provider.CreateInboxScope(inbox);
            await scope.ServiceProvider.GetRequiredService<DeleteCompletedMessages<InboxMessage>>().ExecuteAsync(cancellationToken);
        }

        // Assert
        var stored = await ReadMessagesAsync(provider, cancellationToken);
        Assert.Equal([Shipping], stored.Select(m => m.Inbox), StringComparer.Ordinal);
    }

    [Fact]
    public void HostStart_Throws_WhenAHandlerIsBoundToAnUnregisteredInbox()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInboxServices<InboxOutboxDbContext>(Payments, _ => { });
        services.AddSingleton(Entry("missing", (_, _) => Task.CompletedTask));

        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<UnknownInboxException>(() => provider.GetRequiredService<InboxDirectory>());
        Assert.Contains("'missing'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Registering_Throws_WhenAnInboxNameIsTakenAlready()
    {
        var services = new ServiceCollection();
        services.AddInboxServices<InboxOutboxDbContext>(Payments, _ => { });

        Assert.Throws<ArgumentException>(() => services.AddInboxServices<InboxOutboxDbContext>(Payments, _ => { }));
    }

    private ServiceProvider CreateServiceProvider(bool shippingFails = true)
    {
        var services = new ServiceCollection();
        var loggerFactory = LoggerFactory.Create(builder => builder.ConfigureTestLogger(_testOutputHelper));

        services.AddLogging(builder => builder.ConfigureTestLogger(_testOutputHelper));
        services.AddDbContext<InboxOutboxDbContext>(options => options
            .UseNpgsql(Database.ConnectionString)
            .UseLoggerFactory(loggerFactory));

        services.AddInboxServices<InboxOutboxDbContext>(Payments, cfg => cfg.CompletedMessageRetention = TimeSpan.Zero);
        services.AddInboxServices<InboxOutboxDbContext>(Shipping, _ => { });

        // two Handlers for one message type, which only separate assemblies could declare
        services.AddSingleton(Entry(Payments, (scope, message) =>
        {
            scope.GetRequiredService<InboxOutboxDbContext>().Users.Add(new User { Name = $"payments-{message.Id}" });
            return Task.CompletedTask;
        }));
        services.AddSingleton(Entry(Shipping, (scope, message) =>
        {
            scope.GetRequiredService<InboxOutboxDbContext>().Users.Add(new User { Name = $"shipping-{message.Id}" });
            return shippingFails ? throw new InvalidOperationException("Shipping is down.") : Task.CompletedTask;
        }));

        return services.BuildServiceProvider();
    }

    private static HandlerEntry<InboxMessage> Entry(string inbox, Func<IServiceProvider, FannedOutMessage, Task> handle) =>
        new(
            inbox,
            typeof(FannedOutMessage).FullName!,
            // the delegate below is the Handler; the type only labels it
            typeof(NamedInboxTests),
            typeof(FannedOutMessage),
            (scope, message, _, _) => handle(scope, System.Text.Json.JsonSerializer.Deserialize<FannedOutMessage>(message.Data)!));

    private static async Task StageAsync(ServiceProvider provider, IEnumerable<InboxMessage> messages, CancellationToken cancellationToken)
    {
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<InboxOutboxDbContext>();
        var inbox = scope.ServiceProvider.GetRequiredService<IInbox>();

        await context.ExecuteInTransactionAsync(ct => inbox.AddMessagesAsync(context, messages, ct), cancellationToken);
    }

    private static async Task<List<InboxMessage>> ReadMessagesAsync(ServiceProvider provider, CancellationToken cancellationToken)
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<InboxOutboxDbContext>().InboxMessages
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    private sealed record FannedOutMessage(int Id);

    private sealed record UnhandledMessage;
}
