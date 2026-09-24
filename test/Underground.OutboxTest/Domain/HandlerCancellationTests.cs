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
/// A cancellation the Handler raised itself - an <c>HttpClient</c> timeout, say - is not a shutdown. It has to
/// be recorded like any other failure rather than travel out of the worker and end it.
/// </summary>
public class HandlerCancellationTests : DatabaseTest
{
    private readonly ITestOutputHelper _testOutputHelper;

    public HandlerCancellationTests(ITestOutputHelper testOutputHelper) : base(testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
    }

    [Fact]
    public async Task OutboxHandlerOwnCancellationIsRecordedAsAFailedAttempt()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateServiceProvider(_ => { });
        var context = provider.GetRequiredService<InboxOutboxDbContext>();
        await AddOutboxMessageAsync(provider, context, cancellationToken);

        // Act: a worker drives exactly this, so a throw here is a worker lost
        var result = await provider.GetRequiredService<ConcurrentProcessor<OutboxMessage>>().ProcessNextAsync(cancellationToken);

        // Assert
        Assert.Equal(ClaimResult.HeadMessageClaimed, result);

        var message = await context.OutboxMessages.AsNoTracking().SingleAsync(cancellationToken);
        Assert.Null(message.CompletedAt);
        Assert.Equal(1, message.RetryCount);
    }

    [Fact]
    public async Task OutboxHandlerOwnCancellationReachesTheExceptionPolicies()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateServiceProvider(cfg => cfg.Policies.OnException<TaskCanceledException>().Discard());
        var context = provider.GetRequiredService<InboxOutboxDbContext>();
        await AddOutboxMessageAsync(provider, context, cancellationToken);

        // Act
        await provider.GetRequiredService<ConcurrentProcessor<OutboxMessage>>().ProcessUntilIdleAsync(cancellationToken);

        // Assert
        Assert.Empty(await context.OutboxMessages.AsNoTracking().ToListAsync(cancellationToken));
    }

    [Fact]
    public async Task InboxHandlerOwnCancellationIsRecordedAsAFailedAttempt()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateServiceProvider(_ => { });
        var context = provider.GetRequiredService<InboxOutboxDbContext>();

        await context.ExecuteInTransactionAsync(
            ct => provider.GetRequiredService<IInbox>().AddMessageAsync(
                context,
                new InboxMessage(Guid.NewGuid(), DateTime.UtcNow, new SelfCancellingMessage(2)),
                ct),
            cancellationToken);

        // Act
        var result = await provider.GetRequiredService<ConcurrentProcessor<InboxMessage>>().ProcessNextAsync(cancellationToken);

        // Assert: the savepoint was rolled back and the attempt still committed
        Assert.Equal(ClaimResult.HeadMessageClaimed, result);

        var message = await context.InboxMessages.AsNoTracking().SingleAsync(cancellationToken);
        Assert.Null(message.CompletedAt);
        Assert.Equal(1, message.RetryCount);
    }

    private ServiceProvider CreateServiceProvider(Action<OutboxServiceConfiguration> configureOutbox)
    {
        var services = new ServiceCollection();
        var loggerFactory = LoggerFactory.Create(builder => builder.ConfigureTestLogger(_testOutputHelper));

        services.AddLogging(builder => builder.ConfigureTestLogger(_testOutputHelper));
        services.AddDbContext<InboxOutboxDbContext>(options => options
            .UseNpgsql(Database.ConnectionString)
            .UseLoggerFactory(loggerFactory));
        services.AddOutboxServices<InboxOutboxDbContext>(configureOutbox);
        services.AddInboxServices<InboxOutboxDbContext>(_ => { });
        services.AddUndergroundOutboxTestMessageHandlers();

        return services.BuildServiceProvider();
    }

    private static Task AddOutboxMessageAsync(IServiceProvider provider, InboxOutboxDbContext context, CancellationToken cancellationToken)
        => context.ExecuteInTransactionAsync(
            ct => provider.GetRequiredService<IOutbox>().AddMessageAsync(
                context,
                new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new SelfCancellingMessage(1)),
                ct),
            cancellationToken);
}
