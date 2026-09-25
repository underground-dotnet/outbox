using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Underground.Outbox;
using Underground.Outbox.Configuration;
using Underground.Outbox.Data;
using Underground.Outbox.Domain;
using Underground.Outbox.Exceptions;
using Underground.OutboxTest.TestHandler;

namespace Underground.OutboxTest.Domain;

/// <summary>
/// The inbox holds its Claim as a row lock and guards its outcome write on the row the claim handed out, so
/// that row has to be the version the lock was taken on, and a write that misses anyway must not commit.
/// </summary>
public class InboxClaimTests : DatabaseTest
{
    private readonly ITestOutputHelper _testOutputHelper;

    public InboxClaimTests(ITestOutputHelper testOutputHelper) : base(testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;

        InboxRetryMessageHandler.Reset();
    }

    [Fact]
    public async Task ClaimHandsOutTheRowItLocked_WhenARetryCommitsDuringTheClaim()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var message = new InboxMessage(Guid.NewGuid(), DateTime.UtcNow, new InboxRetryMessage(1));

        var retryDuringClaim = new RewriteCommandInterceptor("SKIP LOCKED", async (claim, ct) =>
        {
            // a cursor keeps the snapshot it was declared with but locks rows only as it fetches them, so
            // the retry below commits after the claim's snapshot and before its row lock
            await using (var declare = claim.Connection!.CreateCommand())
            {
                declare.Transaction = claim.Transaction;
                declare.CommandText = $"DECLARE claim_cursor NO SCROLL CURSOR FOR {claim.CommandText}";
                await declare.ExecuteNonQueryAsync(ct);
            }

            // another worker's retry, whose backoff has already run out
            await using var otherWorker = CreateDbContext();
            await otherWorker.Database.ExecuteSqlAsync(
                $"UPDATE inbox SET retry_count = retry_count + 1, visible_at = clock_timestamp() WHERE id = {message.Id}",
                ct);

            claim.CommandText = "FETCH ALL FROM claim_cursor";
        });

        await using var provider = CreateServiceProvider(retryDuringClaim);
        await AddMessageAsync(provider, message, cancellationToken);

        // Act
        await ProcessOnceAsync(provider, cancellationToken);

        // Assert
        Assert.True(retryDuringClaim.Rewritten);

        await using var verify = provider.CreateAsyncScope();
        var context = verify.ServiceProvider.GetRequiredService<InboxOutboxDbContext>();
        var stored = await context.InboxMessages.AsNoTracking().SingleAsync(cancellationToken);

        Assert.NotNull(stored.CompletedAt);
        Assert.Equal(1, stored.RetryCount);
        Assert.Equal(1, await context.Users.CountAsync(cancellationToken));
        Assert.Equal(1, InboxRetryMessageHandler.Calls);
    }

    [Fact]
    public async Task HandlersWritesAreRolledBack_WhenTheCompletionMatchesNoRow()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var message = new InboxMessage(Guid.NewGuid(), DateTime.UtcNow, new InboxRetryMessage(2));

        var completionMisses = new RewriteCommandInterceptor("SET completed_at", (completion, _) =>
        {
            // as if the claim had handed out some other version of the row than the one it locked
            completion.Parameters["lease"].Value = DateTime.UnixEpoch;
            return Task.CompletedTask;
        });

        await using var provider = CreateServiceProvider(completionMisses);
        await AddMessageAsync(provider, message, cancellationToken);

        // Act
        await Assert.ThrowsAsync<InboxClaimLostException>(() => ProcessOnceAsync(provider, cancellationToken));

        // Assert: nothing the attempt did was kept, so the message is still pending and offered again
        Assert.True(completionMisses.Rewritten);

        await using (var verify = provider.CreateAsyncScope())
        {
            var context = verify.ServiceProvider.GetRequiredService<InboxOutboxDbContext>();
            var stored = await context.InboxMessages.AsNoTracking().SingleAsync(cancellationToken);

            Assert.Null(stored.CompletedAt);
            Assert.Equal(0, stored.RetryCount);
            Assert.Equal(0, await context.Users.CountAsync(cancellationToken));
        }

        await ProcessOnceAsync(provider, cancellationToken);

        await using (var verify = provider.CreateAsyncScope())
        {
            var context = verify.ServiceProvider.GetRequiredService<InboxOutboxDbContext>();

            Assert.Equal(1, await context.InboxMessages.CountAsync(m => m.CompletedAt != null, cancellationToken));
            Assert.Equal(1, await context.Users.CountAsync(cancellationToken));
            Assert.Equal(2, InboxRetryMessageHandler.Calls);
        }
    }

    private static async Task AddMessageAsync(ServiceProvider provider, InboxMessage message, CancellationToken cancellationToken)
    {
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<InboxOutboxDbContext>();
        var inbox = scope.ServiceProvider.GetRequiredService<IInbox>();

        await context.ExecuteInTransactionAsync(ct => inbox.AddMessageAsync(context, message, ct), cancellationToken);
    }

    private static async Task ProcessOnceAsync(ServiceProvider provider, CancellationToken cancellationToken)
    {
        await using var scope = provider.CreateAsyncScope();
        var processor = scope.ServiceProvider.GetRequiredService<IProcessor<InboxMessage>>();

        await processor.TryProcessHeadMessageAsync(scope, cancellationToken);
    }

    private ServiceProvider CreateServiceProvider(IInterceptor interceptor)
    {
        var services = new ServiceCollection();
        var loggerFactory = LoggerFactory.Create(builder => builder.ConfigureTestLogger(_testOutputHelper));

        services.AddLogging(builder => builder.ConfigureTestLogger(_testOutputHelper));
        services.AddDbContext<InboxOutboxDbContext>(options => options
            .UseNpgsql(Database.ConnectionString)
            .UseLoggerFactory(loggerFactory)
            .AddInterceptors(interceptor));
        services.AddOutboxServices<InboxOutboxDbContext>(_ => { });
        services.AddInboxServices<InboxOutboxDbContext>(_ => { });
        services.AddUndergroundOutboxTestMessageHandlers();

        return services.BuildServiceProvider();
    }
}
