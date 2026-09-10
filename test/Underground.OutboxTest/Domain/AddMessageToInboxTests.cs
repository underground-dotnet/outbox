using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Underground.Outbox;
using Underground.Outbox.Configuration;
using Underground.Outbox.Data;
using Underground.OutboxTest.TestHandler;

namespace Underground.OutboxTest.Domain;

[Collection("AddMessageToInboxTests Collection")]
public class AddMessageToInboxTests : DatabaseTest
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILoggerFactory _loggerFactory;

    public AddMessageToInboxTests(ITestOutputHelper testOutputHelper) : base(testOutputHelper)
    {
        _loggerFactory = LoggerFactory.Create(builder => builder.ConfigureTestLogger(testOutputHelper));

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging(builder => builder.ConfigureTestLogger(testOutputHelper));
        serviceCollection.AddDbContext<InboxOutboxDbContext>(options => ConfigureOptions(options));
        serviceCollection.AddInboxServices<InboxOutboxDbContext>(cfg => { });

        _serviceProvider = serviceCollection.BuildServiceProvider();
    }

    private DbContextOptionsBuilder ConfigureOptions(DbContextOptionsBuilder options) =>
        options
            .UseNpgsql(Database.ConnectionString)
            .UseLoggerFactory(_loggerFactory)
            .EnableSensitiveDataLogging();

    private InboxOutboxDbContext CreateInboxDbContext() =>
        new((DbContextOptions<InboxOutboxDbContext>)ConfigureOptions(new DbContextOptionsBuilder<InboxOutboxDbContext>()).Options);

    [Fact]
    public async Task StageMessage_IsWrittenByTheCallersSave_InsideACallerBegunTransaction()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var context = CreateInboxDbContext();
        var inbox = _serviceProvider.GetRequiredService<IInbox>();
        var message = new InboxMessage(Guid.NewGuid(), DateTime.UtcNow, new ExampleMessage(1));

        // Act
        await using (var transaction = await context.Database.BeginTransactionAsync(cancellationToken))
        {
            inbox.StageMessage(context, message);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        // Assert
        await using var assertContext = CreateInboxDbContext();
        Assert.Single(await assertContext.InboxMessages.Where(m => m.EventId == message.EventId).ToListAsync(cancellationToken));
    }

    [Fact]
    public async Task StageMessage_IsWrittenByTheCallersSave_WithoutATransaction()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var context = CreateInboxDbContext();
        var inbox = _serviceProvider.GetRequiredService<IInbox>();
        var message = new InboxMessage(Guid.NewGuid(), DateTime.UtcNow, new ExampleMessage(2));

        // Act
        inbox.StageMessage(context, message);
        await context.SaveChangesAsync(cancellationToken);

        // Assert
        await using var assertContext = CreateInboxDbContext();
        Assert.Single(await assertContext.InboxMessages.Where(m => m.EventId == message.EventId).ToListAsync(cancellationToken));
    }
}
