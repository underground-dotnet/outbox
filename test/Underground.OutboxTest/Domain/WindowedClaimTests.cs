using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Underground.Outbox;
using Underground.Outbox.Configuration;
using Underground.Outbox.Data;
using Underground.Outbox.Domain;
using Underground.OutboxTest.TestHandler;

namespace Underground.OutboxTest.Domain;

/// <summary>
/// The claim looks among the oldest pending messages first and considers every Group only when nothing
/// there can be claimed and messages lie beyond them. These tests fill that window with a Group whose
/// HeadMessage is scheduled ahead, so whatever can be claimed lies beyond it.
/// </summary>
public class WindowedClaimTests : DatabaseTest
{
    private const int BlockedGroupSize = ClaimHeadMessage<OutboxMessage>.HeadMessageWindow + 1;
    private const int BeyondTheWindow = BlockedGroupSize + 1;

    private readonly ITestOutputHelper _testOutputHelper;

    public WindowedClaimTests(ITestOutputHelper testOutputHelper) : base(testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;

        BacklogMessageHandler.CalledWith.Clear();
    }

    [Fact]
    public async Task OutboxClaimReachesAHeadMessageBeyondAWindowWithNothingClaimable()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateServiceProvider();
        var context = provider.GetRequiredService<InboxOutboxDbContext>();
        var outbox = provider.GetRequiredService<IOutbox>();

        await context.ExecuteInTransactionAsync(
            ct => outbox.AddMessagesAsync(context, BlockedGroup((id, visibleAt) => new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new BacklogMessage(id), "blocked", visibleAt)), ct),
            cancellationToken);
        await context.ExecuteInTransactionAsync(
            ct => outbox.AddMessageAsync(context, new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new BacklogMessage(BeyondTheWindow), "healthy"), ct),
            cancellationToken);

        // Act
        await provider.GetRequiredService<ConcurrentProcessor<OutboxMessage>>().ProcessUntilIdleAsync(cancellationToken);

        // Assert
        Assert.Equal([BeyondTheWindow], BacklogMessageHandler.CalledWith);
        Assert.Equal(1, await context.OutboxMessages.CountAsync(m => m.CompletedAt != null, cancellationToken));
    }

    [Fact]
    public async Task InboxClaimReachesAHeadMessageBeyondAWindowWithNothingClaimable()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateServiceProvider();
        var context = provider.GetRequiredService<InboxOutboxDbContext>();
        var inbox = provider.GetRequiredService<IInbox>();

        await context.ExecuteInTransactionAsync(
            ct => inbox.AddMessagesAsync(context, BlockedGroup((id, visibleAt) => new InboxMessage(Guid.NewGuid(), DateTime.UtcNow, new BacklogMessage(id), "blocked", visibleAt)), ct),
            cancellationToken);
        await context.ExecuteInTransactionAsync(
            ct => inbox.AddMessageAsync(context, new InboxMessage(Guid.NewGuid(), DateTime.UtcNow, new BacklogMessage(BeyondTheWindow), "healthy"), ct),
            cancellationToken);

        // Act
        await provider.InboxWorkers().ProcessUntilIdleAsync(cancellationToken);

        // Assert
        Assert.Equal([BeyondTheWindow], BacklogMessageHandler.CalledWith);
        Assert.Equal(1, await context.InboxMessages.CountAsync(m => m.CompletedAt != null, cancellationToken));
    }

    [Fact]
    public async Task ClaimTakesOneStatement_WhenTheOutboxIsEmpty()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var claims = new CountCommandInterceptor("SKIP LOCKED");
        await using var provider = CreateServiceProvider(claims);

        // Act
        var claimed = await ClaimOutboxHeadMessageAsync(provider, cancellationToken);

        // Assert
        Assert.Null(claimed);
        Assert.Equal(1, claims.Count);
    }

    [Fact]
    public async Task ClaimConsidersEveryGroupOnlyWhenMessagesLieBeyondAFullWindow()
    {
        // Arrange: exactly a window's worth of messages, none of them claimable
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateServiceProvider();
        var context = provider.GetRequiredService<InboxOutboxDbContext>();
        var outbox = provider.GetRequiredService<IOutbox>();

        await context.ExecuteInTransactionAsync(
            ct => outbox.AddMessagesAsync(context, BlockedGroup(ClaimHeadMessage<OutboxMessage>.HeadMessageWindow, (id, visibleAt) => new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new BacklogMessage(id), "blocked", visibleAt)), ct),
            cancellationToken);

        // Act & Assert: nothing lies beyond the window, so there is nothing to claim
        Assert.Null(await ClaimOutboxHeadMessageAsync(provider, cancellationToken));

        // Act & Assert: the message right after the window is reached
        await context.ExecuteInTransactionAsync(
            ct => outbox.AddMessageAsync(context, new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new BacklogMessage(BeyondTheWindow), "healthy"), ct),
            cancellationToken);

        var claimed = await ClaimOutboxHeadMessageAsync(provider, cancellationToken);

        Assert.Equal("healthy", claimed?.GroupKey);
    }

    /// <summary>
    /// More messages than the window holds, in one Group whose HeadMessage is scheduled ahead: every one of
    /// them is either that HeadMessage or behind it, so none can be claimed.
    /// </summary>
    private static List<T> BlockedGroup<T>(Func<int, DateTime?, T> create)
        => BlockedGroup(BlockedGroupSize, create);

    private static List<T> BlockedGroup<T>(int size, Func<int, DateTime?, T> create)
        => [.. Enumerable.Range(1, size).Select(id => create(id, id == 1 ? DateTime.UtcNow.AddHours(1) : null))];

    private static async Task<OutboxMessage?> ClaimOutboxHeadMessageAsync(ServiceProvider provider, CancellationToken cancellationToken)
    {
        await using var scope = provider.CreateAsyncScope();

        var claim = await scope.ServiceProvider.GetRequiredService<ClaimHeadMessage<OutboxMessage>>().ExecuteAsync(cancellationToken);

        return claim?.Message;
    }

    private ServiceProvider CreateServiceProvider(params IInterceptor[] interceptors)
    {
        var services = new ServiceCollection();
        var loggerFactory = LoggerFactory.Create(builder => builder.ConfigureTestLogger(_testOutputHelper));

        services.AddLogging(builder => builder.ConfigureTestLogger(_testOutputHelper));
        services.AddDbContext<InboxOutboxDbContext>(options => options
            .UseNpgsql(Database.ConnectionString)
            .UseLoggerFactory(loggerFactory)
            .AddInterceptors(interceptors));
        services.AddOutboxServices<InboxOutboxDbContext>(_ => { });
        services.AddInboxServices<InboxOutboxDbContext>(TestInbox.Name, _ => { });
        services.AddUndergroundOutboxTestMessageHandlers(TestInbox.Name);

        return services.BuildServiceProvider();
    }
}
