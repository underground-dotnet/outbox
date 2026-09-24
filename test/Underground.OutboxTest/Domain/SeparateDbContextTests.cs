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
/// The inbox and the outbox in different DbContexts, neither mapping the other's table. Each side has to
/// work against its own context whichever was registered last.
/// </summary>
public class SeparateDbContextTests : DatabaseTest
{
    private readonly ITestOutputHelper _testOutputHelper;

    public SeparateDbContextTests(ITestOutputHelper testOutputHelper) : base(testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;

        SeparateContextMessageHandler.CalledWith.Clear();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EachSideProcessesAgainstItsOwnContext(bool outboxRegisteredFirst)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateServiceProvider(outboxRegisteredFirst);
        using var scope = provider.CreateScope();

        var outboxContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        await outboxContext.ExecuteInTransactionAsync(
            ct => scope.ServiceProvider.GetRequiredService<IOutbox>().AddMessageAsync(
                outboxContext,
                new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new SeparateContextMessage(1)),
                ct),
            cancellationToken);

        var inboxContext = scope.ServiceProvider.GetRequiredService<InboxOnlyDbContext>();
        await inboxContext.ExecuteInTransactionAsync(
            ct => scope.ServiceProvider.GetRequiredService<IInbox>().AddMessageAsync(
                inboxContext,
                new InboxMessage(Guid.NewGuid(), DateTime.UtcNow, new SeparateContextMessage(2)),
                ct),
            cancellationToken);

        // Act
        await provider.GetRequiredService<ConcurrentProcessor<OutboxMessage>>().ProcessUntilIdleAsync(cancellationToken);
        await provider.GetRequiredService<ConcurrentProcessor<InboxMessage>>().ProcessUntilIdleAsync(cancellationToken);

        // Assert
        Assert.Equal([1, 2], SeparateContextMessageHandler.CalledWith.Order());
        Assert.NotNull((await outboxContext.OutboxMessages.AsNoTracking().SingleAsync(cancellationToken)).CompletedAt);
        Assert.NotNull((await inboxContext.InboxMessages.AsNoTracking().SingleAsync(cancellationToken)).CompletedAt);
    }

    private ServiceProvider CreateServiceProvider(bool outboxRegisteredFirst)
    {
        var services = new ServiceCollection();
        var loggerFactory = LoggerFactory.Create(builder => builder.ConfigureTestLogger(_testOutputHelper));

        services.AddLogging(builder => builder.ConfigureTestLogger(_testOutputHelper));
        services.AddDbContext<TestDbContext>(options => options.UseNpgsql(Database.ConnectionString).UseLoggerFactory(loggerFactory));
        services.AddDbContext<InboxOnlyDbContext>(options => options.UseNpgsql(Database.ConnectionString).UseLoggerFactory(loggerFactory));

        if (outboxRegisteredFirst)
        {
            services.AddOutboxServices<TestDbContext>(_ => { });
            services.AddInboxServices<InboxOnlyDbContext>(_ => { });
        }
        else
        {
            services.AddInboxServices<InboxOnlyDbContext>(_ => { });
            services.AddOutboxServices<TestDbContext>(_ => { });
        }

        services.AddUndergroundOutboxTestMessageHandlers();

        return services.BuildServiceProvider();
    }
}
