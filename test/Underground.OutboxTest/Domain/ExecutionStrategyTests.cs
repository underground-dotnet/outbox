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
/// The library under a host that configured a retrying Execution Strategy, as Aspire's
/// <c>EnrichNpgsqlDbContext</c> does. See ADR 0006.
/// </summary>
public class ExecutionStrategyTests : DatabaseTest
{
    private readonly ITestOutputHelper _testOutputHelper;

    public ExecutionStrategyTests(ITestOutputHelper testOutputHelper) : base(testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;

        RetryMessageHandler.Reset();
        InboxRetryMessageHandler.Reset();
    }

    /// <summary>
    /// Builds a provider whose context retries <see cref="TransientTestException"/>, optionally failing one
    /// statement on the way.
    /// </summary>
    private ServiceProvider CreateServiceProvider(TransientFaultInterceptor? fault = null)
    {
        var services = new ServiceCollection();

        services.AddLogging(builder => builder.ConfigureTestLogger(_testOutputHelper));

        var loggerFactory = LoggerFactory.Create(builder => builder.ConfigureTestLogger(_testOutputHelper));

        services.AddDbContext<InboxOutboxDbContext>(options =>
        {
            options
                .UseNpgsql(Database.ConnectionString, npgsql => npgsql.ExecutionStrategy(deps => new TransientTestExecutionStrategy(deps)))
                .UseLoggerFactory(loggerFactory)
                .EnableSensitiveDataLogging();

            if (fault is not null)
            {
                options.AddInterceptors(fault);
            }
        });

        services.AddOutboxServices<InboxOutboxDbContext>(cfg => cfg.AddHandler<RetryMessageHandler, RetryMessage>());
        services.AddInboxServices<InboxOutboxDbContext>(cfg => cfg.AddHandler<InboxRetryMessageHandler, InboxRetryMessage>());

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task AddMessageAsync_IsRefused_InACallerBegunTransaction_UnderARetryingExecutionStrategy()
    {
        // the failure this change exists to remove: EF refuses any strategy-covered operation inside a
        // transaction the caller began itself, which is every way of staging an outbox message
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateServiceProvider();
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<InboxOutboxDbContext>();
        var outbox = scope.ServiceProvider.GetRequiredService<IOutbox>();

        var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await using (transaction.ConfigureAwait(false))
        {
            var refused = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await outbox.AddMessageAsync(
                    context,
                    new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new RetryMessage(0)),
                    cancellationToken));

            Assert.Contains("does not support user-initiated transactions", refused.Message, StringComparison.Ordinal);

            await transaction.RollbackAsync(cancellationToken);
        }
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_AddsAndDelivers_UnderARetryingExecutionStrategy()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateServiceProvider();
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<InboxOutboxDbContext>();
        var outbox = scope.ServiceProvider.GetRequiredService<IOutbox>();

        // Act
        await context.ExecuteInTransactionAsync(
            ct => outbox.AddMessageAsync(context, new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new RetryMessage(1)), ct),
            cancellationToken);

        await ProcessUntilAsync<OutboxMessage>(provider, () => RetryMessageHandler.Calls > 0, cancellationToken);

        // Assert
        Assert.Equal(1, RetryMessageHandler.Calls);
        Assert.Equal(1, await CompletedCountAsync("outbox", cancellationToken));
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_JoinsAnOpenTransaction_RatherThanNestingOne()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateServiceProvider();
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<InboxOutboxDbContext>();
        var outbox = scope.ServiceProvider.GetRequiredService<IOutbox>();

        // Act: the outer call owns the transaction, so the inner one must find and reuse it
        await context.ExecuteInTransactionAsync(async ct =>
        {
            var outer = context.Database.CurrentTransaction;

            await context.ExecuteInTransactionAsync(async inner =>
            {
                Assert.Same(outer, context.Database.CurrentTransaction);
                await outbox.AddMessageAsync(context, new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new RetryMessage(2)), inner);
            }, ct);
        }, cancellationToken);

        // Assert: the inner call committed nothing of its own, and the outer commit made the message durable
        await ProcessUntilAsync<OutboxMessage>(provider, () => RetryMessageHandler.Calls > 0, cancellationToken);
        Assert.Equal(1, RetryMessageHandler.Calls);
    }

    [Fact]
    public async Task OutboxHandlerIsNotReplayed_WhenTheCompletionWriteFails()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var fault = new TransientFaultInterceptor("SET completed_at", faults: 1);
        await using var provider = CreateServiceProvider(fault);
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<InboxOutboxDbContext>();
        var outbox = scope.ServiceProvider.GetRequiredService<IOutbox>();

        await context.ExecuteInTransactionAsync(
            ct => outbox.AddMessageAsync(context, new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new RetryMessage(3)), ct),
            cancellationToken);

        // Act
        await ProcessUntilAsync<OutboxMessage>(provider, () => RetryMessageHandler.Calls > 0, cancellationToken);

        // Assert: the outbox wraps only its claim, so the failure ends the attempt rather than replaying
        // the Handler. The outcome write is not retried either - the Lease expires and the message comes
        // back, which is the redelivery at-least-once already allows for.
        Assert.Equal(1, fault.Injected);
        Assert.Equal(1, RetryMessageHandler.Calls);
        Assert.Equal(0, await CompletedCountAsync("outbox", cancellationToken));
    }

    [Fact]
    public async Task InboxHandlerIsReplayed_ButWritesItsRowOnce_WhenTheCompletionWriteFailsTransiently()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var fault = new TransientFaultInterceptor("SET completed_at", faults: 1);
        await using var provider = CreateServiceProvider(fault);
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<InboxOutboxDbContext>();
        var inbox = scope.ServiceProvider.GetRequiredService<IInbox>();

        await context.ExecuteInTransactionAsync(
            ct => inbox.AddMessageAsync(context, new InboxMessage(Guid.NewGuid(), DateTime.UtcNow, new InboxRetryMessage(4)), ct),
            cancellationToken);

        // Act
        await ProcessUntilAsync<InboxMessage>(provider, () => InboxRetryMessageHandler.Calls > 0, cancellationToken);

        // Assert: the whole attempt is the inbox's retry unit, so the handler ran twice - but the first run's
        // row went back with its transaction, which is what "exactly once" now claims
        Assert.Equal(1, fault.Injected);
        Assert.Equal(2, InboxRetryMessageHandler.Calls);
        Assert.Equal(1, await UserCountAsync(cancellationToken));
        Assert.Equal(1, await CompletedCountAsync("inbox", cancellationToken));
    }

    /// <summary>
    /// Drives the processor until <paramref name="handled"/> reports the message was picked up, or the
    /// budget runs out. A claim only offers Stable messages, and the watermark that decides stability is
    /// cluster-wide, so a write transaction in another test's database can withhold this one for a moment.
    /// </summary>
    private static async Task ProcessUntilAsync<TEntity>(IServiceProvider provider, Func<bool> handled, CancellationToken cancellationToken)
        where TEntity : class, IMessage
    {
        var processor = provider.GetRequiredService<ConcurrentProcessor<TEntity>>();
        var deadline = DateTime.UtcNow.AddSeconds(10);

        do
        {
            await processor.ProcessUntilIdleAsync(cancellationToken);
        }
        while (!handled() && DateTime.UtcNow < deadline && await DelayAsync(cancellationToken));
    }

    private static async Task<bool> DelayAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
        return true;
    }

    private async Task<int> CompletedCountAsync(string table, CancellationToken cancellationToken)
    {
        await using var context = CreateDbContext();
        var sql = $"""SELECT count(*)::int AS "Value" FROM public.{table} WHERE completed_at IS NOT NULL""";

        return await context.Database.SqlQueryRaw<int>(sql).SingleAsync(cancellationToken);
    }

    private async Task<int> UserCountAsync(CancellationToken cancellationToken)
    {
        await using var context = CreateDbContext();

        return await context.Users.CountAsync(cancellationToken);
    }
}
