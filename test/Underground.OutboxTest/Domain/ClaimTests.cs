using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Underground.Outbox;
using Underground.Outbox.Configuration;
using Underground.Outbox.Data;
using Underground.Outbox.Domain;
using Underground.OutboxTest.TestHandler;

namespace Underground.OutboxTest.Domain;

/// <summary>
/// A Claim is the only way to write about a claimed message, and every write is guarded on the Lease it holds.
/// These tests drive it directly, without a Handler: one that still holds its Lease writes, one whose Lease
/// another worker has since taken writes nothing.
/// </summary>
public class ClaimTests : DatabaseTest
{
    private static readonly TimeSpan Delay = TimeSpan.FromMinutes(10);

    private readonly ITestOutputHelper _testOutputHelper;

    public ClaimTests(ITestOutputHelper testOutputHelper) : base(testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
    }

    [Fact]
    public async Task ClaimThatStillHoldsItsLeaseCompletesTheMessage()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateServiceProvider();
        await AddOutboxMessageAsync(provider, cancellationToken);

        await using var held = await ClaimOutboxAsync(provider, cancellationToken);

        Assert.True(await held.Claim.TryCompleteAsync(cancellationToken));

        var stored = await CreateDbContext().OutboxMessages.AsNoTracking().SingleAsync(cancellationToken);
        Assert.NotNull(stored.CompletedAt);
    }

    [Fact]
    public async Task RecordedFailureCountsTheAttemptAndHidesTheMessageForTheDelay()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateServiceProvider();
        await AddOutboxMessageAsync(provider, cancellationToken);

        await using var held = await ClaimOutboxAsync(provider, cancellationToken);

        Assert.True(await held.Claim.TryRecordFailureAsync(Delay, cancellationToken));

        var context = CreateDbContext();
        var stored = await context.OutboxMessages.AsNoTracking().SingleAsync(cancellationToken);
        Assert.Equal(1, stored.RetryCount);
        Assert.Null(stored.CompletedAt);
        Assert.InRange(await context.SecondsUntilVisibleAsync(stored.Id, cancellationToken), Delay.TotalSeconds - 5, Delay.TotalSeconds);
    }

    /// <summary>
    /// The guard moves with the Lease: after a renewal the completion is guarded on the renewed instant, not
    /// the one the claim granted, and still lands.
    /// </summary>
    [Fact]
    public async Task RenewedClaimStillCompletesTheMessage()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateServiceProvider();
        await AddOutboxMessageAsync(provider, cancellationToken);

        await using var held = await ClaimOutboxAsync(provider, cancellationToken);
        var context = CreateDbContext();
        var granted = await context.VisibleAtAsync(held.Claim.Message.Id, cancellationToken);

        await using (var renewalScope = provider.CreateAsyncScope())
        {
            var renewalConnection = renewalScope.ServiceProvider.GetRequiredService<IOutboxDbContext>();
            Assert.True(await held.Claim.TryRenewAsync(renewalConnection, Delay, cancellationToken));
        }

        Assert.NotEqual(granted, await context.VisibleAtAsync(held.Claim.Message.Id, cancellationToken));
        Assert.True(await held.Claim.TryCompleteAsync(cancellationToken));

        var stored = await context.OutboxMessages.AsNoTracking().SingleAsync(cancellationToken);
        Assert.NotNull(stored.CompletedAt);
    }

    /// <summary>
    /// A worker whose Lease expired and was taken over must not touch the message: not complete it, not record
    /// an attempt against it, and not renew the Lease out from under the worker that holds it now.
    /// </summary>
    [Fact]
    public async Task ClaimWhoseLeaseWasTakenOverWritesNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateServiceProvider();
        await AddOutboxMessageAsync(provider, cancellationToken);
        var context = CreateDbContext();

        await using var stale = await ClaimOutboxAsync(provider, cancellationToken);
        await context.MakeIncompleteMessagesVisibleAsync(cancellationToken);
        await using var current = await ClaimOutboxAsync(provider, cancellationToken);
        var currentLease = await context.VisibleAtAsync(current.Claim.Message.Id, cancellationToken);

        await using var renewalScope = provider.CreateAsyncScope();
        var renewalConnection = renewalScope.ServiceProvider.GetRequiredService<IOutboxDbContext>();

        Assert.False(await stale.Claim.TryCompleteAsync(cancellationToken));
        Assert.False(await stale.Claim.TryRecordFailureAsync(Delay, cancellationToken));
        Assert.False(await stale.Claim.TryRenewAsync(renewalConnection, Delay, cancellationToken));

        var stored = await context.OutboxMessages.AsNoTracking().SingleAsync(cancellationToken);
        Assert.Null(stored.CompletedAt);
        Assert.Equal(0, stored.RetryCount);
        Assert.Equal(currentLease, await context.VisibleAtAsync(stored.Id, cancellationToken));
    }

    /// <summary>
    /// The inbox holds its Claim as the row lock inside one transaction, which keeps the same guard satisfied,
    /// and the completion joins that transaction: rolled back, the message is still pending.
    /// </summary>
    [Fact]
    public async Task InboxClaimCompletesTheMessageInsideItsTransaction()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateServiceProvider();

        await using (var addScope = provider.CreateAsyncScope())
        {
            var addContext = addScope.ServiceProvider.GetRequiredService<InboxOutboxDbContext>();
            var inbox = addScope.ServiceProvider.GetRequiredService<IInbox>();
            var message = new InboxMessage(Guid.NewGuid(), DateTime.UtcNow, new InboxRetryMessage(1));

            await addContext.ExecuteInTransactionAsync(ct => inbox.AddMessageAsync(addContext, message, ct), cancellationToken);
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<IInboxDbContext>();
            var claimHeadMessage = scope.ServiceProvider.GetRequiredService<ClaimHeadMessage<InboxMessage>>();

            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            var claim = await claimHeadMessage.ExecuteAsync(cancellationToken);
            Assert.NotNull(claim);

            Assert.True(await claim.TryCompleteAsync(cancellationToken));
            Assert.NotNull(await ReadInboxCompletedAtAsync(context, cancellationToken));

            await transaction.RollbackAsync(cancellationToken);
        }

        await using var verify = provider.CreateAsyncScope();
        var stored = await verify.ServiceProvider.GetRequiredService<InboxOutboxDbContext>()
            .InboxMessages.AsNoTracking().SingleAsync(cancellationToken);
        Assert.Null(stored.CompletedAt);
    }

    private static async Task<DateTime?> ReadInboxCompletedAtAsync(IInboxDbContext context, CancellationToken cancellationToken)
        => await context.InboxMessages.AsNoTracking().Select(m => m.CompletedAt).SingleAsync(cancellationToken);

    private static async Task AddOutboxMessageAsync(ServiceProvider provider, CancellationToken cancellationToken)
    {
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<InboxOutboxDbContext>();
        var outbox = scope.ServiceProvider.GetRequiredService<IOutbox>();
        var message = new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new BlockingMessage(1));

        await context.ExecuteInTransactionAsync(ct => outbox.AddMessageAsync(context, message, ct), cancellationToken);
    }

    /// <summary>
    /// Claims the outbox HeadMessage and commits, which grants the Lease. The scope stays open with the Claim,
    /// because the Claim writes through the context it was claimed on.
    /// </summary>
    private static async Task<HeldClaim> ClaimOutboxAsync(ServiceProvider provider, CancellationToken cancellationToken)
    {
        var scope = provider.CreateAsyncScope();

        try
        {
            var context = scope.ServiceProvider.GetRequiredService<IOutboxDbContext>();
            var claimHeadMessage = scope.ServiceProvider.GetRequiredService<ClaimHeadMessage<OutboxMessage>>();

            var claim = await context.ExecuteInTransactionAsync(claimHeadMessage.ExecuteAsync, cancellationToken);
            Assert.NotNull(claim);

            return new HeldClaim(scope, claim);
        }
        catch
        {
            await scope.DisposeAsync();
            throw;
        }
    }

    private ServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();

        services.AddLogging(builder => builder.ConfigureTestLogger(_testOutputHelper));
        services.AddDbContext<InboxOutboxDbContext>(options => options.UseNpgsql(Database.ConnectionString));
        services.AddOutboxServices<InboxOutboxDbContext>(_ => { });
        services.AddInboxServices<InboxOutboxDbContext>(_ => { });
        services.AddUndergroundOutboxTestMessageHandlers();

        return services.BuildServiceProvider();
    }

    private sealed record HeldClaim(AsyncServiceScope Scope, Claim<OutboxMessage> Claim) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Scope.DisposeAsync();
    }
}
