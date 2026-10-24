using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Underground.Outbox;
using Underground.Outbox.Configuration;
using Underground.Outbox.Data;
using Underground.Outbox.Domain;
using Underground.OutboxTest.TestHandler;

namespace Underground.OutboxTest.Domain;

/// <summary>
/// An inbox message type with two Handlers is stored once per Recipient, and each Recipient's copies form
/// Groups of their own, so one Handler failing leaves the other's untouched (ADR 0012).
/// </summary>
public class InboxRecipientTests : DatabaseTest
{
    private const string Ledger = "Underground.OutboxTest.TestHandler.SharedMessageLedgerHandler";
    private const string Audit = "Underground.OutboxTest.TestHandler.SharedMessageAuditHandler";

    private readonly ITestOutputHelper _testOutputHelper;

    public InboxRecipientTests(ITestOutputHelper testOutputHelper) : base(testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;

        SharedMessageLedgerHandler.CalledWith.Clear();
        SharedMessageAuditHandler.CalledWith.Clear();
    }

    [Fact]
    public async Task EachRecipient_HandlesAndCompletesItsOwnCopy()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateServiceProvider();
        await AddAsync(provider, [new SharedMessage(1)], cancellationToken);

        // Act
        await ProcessUntilIdleAsync(provider, cancellationToken);

        // Assert
        Assert.Equal([1], SharedMessageLedgerHandler.CalledWith);
        Assert.Equal([1], SharedMessageAuditHandler.CalledWith);
        Assert.All(await StoredAsync(provider, cancellationToken), m => Assert.NotNull(m.CompletedAt));
    }

    [Fact]
    public async Task AFailingRecipient_HoldsBackOnlyItsOwnGroup()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateServiceProvider();
        await AddAsync(provider, [new SharedMessage(1, AuditFails: true)], cancellationToken);
        await AddAsync(provider, [new SharedMessage(2)], cancellationToken);

        // Act
        await ProcessUntilIdleAsync(provider, cancellationToken);

        // Assert: the ledger handled both, the audit stalled on its poison message and holds its second copy back
        Assert.Equal([1, 2], SharedMessageLedgerHandler.CalledWith);
        Assert.Equal([1], SharedMessageAuditHandler.CalledWith);

        var stored = await StoredAsync(provider, cancellationToken);
        Assert.All(AddressedTo(stored, Ledger), m => Assert.NotNull(m.CompletedAt));
        Assert.Equal([(1, false), (0, false)], AddressedTo(stored, Audit).Select(m => (m.RetryCount, m.CompletedAt is not null)));
    }

    [Fact]
    public async Task EachRecipient_HandlesItsGroupInOrder()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateServiceProvider();
        await AddAsync(provider, [new SharedMessage(1)], cancellationToken);
        await AddAsync(provider, [new SharedMessage(2), new SharedMessage(3)], cancellationToken);

        // Act
        await ProcessUntilIdleAsync(provider, cancellationToken);

        // Assert
        Assert.Equal([1, 2, 3], SharedMessageLedgerHandler.CalledWith);
        Assert.Equal([1, 2, 3], SharedMessageAuditHandler.CalledWith);
    }

    [Fact]
    public async Task AMessageForAnUnregisteredRecipient_FailsAndRetries_WhileOtherRecipientsProceed()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateServiceProvider();
        await AddAsync(provider, [new SharedMessage(1)], cancellationToken);
        await AddAsync(provider, [new SharedMessage(2)], cancellationToken);

        // as if the audit Handler had been renamed since its messages were stored
        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<InboxOutboxDbContext>().Database.ExecuteSqlAsync(
                $"UPDATE inbox SET recipient = 'Sample.RenamedAuditHandler' WHERE recipient = {Audit}",
                cancellationToken);
        }

        // Act
        await ProcessUntilIdleAsync(provider, cancellationToken);

        // Assert
        Assert.Equal([1, 2], SharedMessageLedgerHandler.CalledWith);
        Assert.Empty(SharedMessageAuditHandler.CalledWith);

        var stored = await StoredAsync(provider, cancellationToken);
        Assert.All(AddressedTo(stored, Ledger), m => Assert.NotNull(m.CompletedAt));
        Assert.Equal([(1, false), (0, false)], AddressedTo(stored, "Sample.RenamedAuditHandler").Select(m => (m.RetryCount, m.CompletedAt is not null)));
    }

    [Fact]
    public async Task TheRecipient_IsOnTheSpanAndInTheLogScope()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var spans = new RecordingTracerProvider();
        var logs = new RecordingLoggerProvider();
        await using var provider = CreateServiceProvider(logs);
        var eventId = Guid.NewGuid();
        await AddAsync(provider, [new SharedMessage(1)], cancellationToken, eventId);

        // Act
        await ProcessUntilIdleAsync(provider, cancellationToken);

        // Assert
        Assert.Equal(
            [Audit, Ledger],
            spans.SpansFor(eventId).Select(span => (string)span.GetTagItem("underground.outbox.recipient")!).Order(StringComparer.Ordinal),
            StringComparer.Ordinal);
        Assert.Contains($"Recipient {Ledger}", logs.Scopes, StringComparer.Ordinal);
        Assert.Contains($"Recipient {Audit}", logs.Scopes, StringComparer.Ordinal);
    }

    private static async Task AddAsync(ServiceProvider provider, SharedMessage[] messages, CancellationToken cancellationToken, Guid? eventId = null)
    {
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<InboxOutboxDbContext>();
        var inbox = scope.ServiceProvider.GetRequiredService<IInbox>();

        await context.ExecuteInTransactionAsync(
            ct => inbox.AddMessagesAsync(context, messages.Select(m => new InboxMessage(eventId ?? Guid.NewGuid(), DateTime.UtcNow, m, "shared")), ct),
            cancellationToken);
    }

    private static Task ProcessUntilIdleAsync(ServiceProvider provider, CancellationToken cancellationToken)
        => provider.GetRequiredService<ConcurrentProcessor<InboxMessage>>().ProcessUntilIdleAsync(cancellationToken);

    private static async Task<List<InboxMessage>> StoredAsync(ServiceProvider provider, CancellationToken cancellationToken)
    {
        await using var scope = provider.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<InboxOutboxDbContext>().InboxMessages
            .AsNoTracking()
            .OrderBy(m => m.Id)
            .ToListAsync(cancellationToken);
    }

    private static IEnumerable<InboxMessage> AddressedTo(List<InboxMessage> stored, string recipient)
        => stored.Where(m => string.Equals(m.Recipient, recipient, StringComparison.Ordinal));

    private ServiceProvider CreateServiceProvider(RecordingLoggerProvider? logs = null)
    {
        var services = new ServiceCollection();
        var loggerFactory = LoggerFactory.Create(builder => builder.ConfigureTestLogger(_testOutputHelper));

        services.AddLogging(builder =>
        {
            builder.ConfigureTestLogger(_testOutputHelper);

            if (logs is not null)
            {
                builder.AddProvider(logs);
            }
        });
        services.AddDbContext<InboxOutboxDbContext>(options => options
            .UseNpgsql(Database.ConnectionString)
            .UseLoggerFactory(loggerFactory));
        services.AddInboxServices<InboxOutboxDbContext>(_ => { });
        services.AddUndergroundOutboxTestMessageHandlers();

        return services.BuildServiceProvider();
    }
}
