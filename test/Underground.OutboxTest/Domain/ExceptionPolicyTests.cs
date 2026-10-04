using System.Data;
using System.Text.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Underground.Outbox;
using Underground.Outbox.Configuration;
using Underground.Outbox.Data;
using Underground.Outbox.Domain;
using Underground.Outbox.Exceptions;
using Underground.OutboxTest.TestHandler;
using Underground.OutboxTest.TestPolicies;

namespace Underground.OutboxTest.Domain;

/// <summary>
/// Every failure of a Processing Attempt that concerns the message reaches the Exception Policies, matched on
/// its cause, except that a Deployment Gap matches only a policy that names one (ADR 0012).
/// </summary>
[Collection("ExampleMessageHandler Collection")]
public class ExceptionPolicyTests : DatabaseTest
{
    private const string NullPayload = "null";

    // valid JSON, so the jsonb column accepts it, but not a PayloadMessage
    private const string MalformedPayload = """{"Id":"not a number"}""";

    private static readonly string PayloadType = typeof(PayloadMessage).FullName!;

    private readonly ITestOutputHelper _testOutputHelper;

    public ExceptionPolicyTests(ITestOutputHelper testOutputHelper) : base(testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;

        BlockingMessageHandler.Reset();
    }

    [Theory]
    [InlineData(MalformedPayload)]
    [InlineData(NullPayload)]
    public async Task Outbox_UnreadablePayload_IsDiscarded_ByGlobalJsonExceptionPolicy(string data)
    {
        await using var provider = CreateServiceProvider(outbox: cfg => cfg.Policies.OnException<JsonException>().Discard());
        await AddAsync(provider, Outbox(PayloadType, data));

        await ProcessOutboxAsync(provider);

        Assert.Empty(await OutboxRowsAsync(provider));
    }

    [Theory]
    [InlineData(MalformedPayload)]
    [InlineData(NullPayload)]
    public async Task Outbox_UnreadablePayload_IsDiscarded_ByHandlerJsonExceptionPolicy(string data)
    {
        await using var provider = CreateServiceProvider(outbox: cfg => cfg
            .ForHandler<PayloadMessageHandler, PayloadMessage>()
            .OnException<JsonException>().Discard());
        await AddAsync(provider, Outbox(PayloadType, data));

        await ProcessOutboxAsync(provider);

        Assert.Empty(await OutboxRowsAsync(provider));
    }

    [Theory]
    [InlineData(MalformedPayload)]
    [InlineData(NullPayload)]
    public async Task Inbox_UnreadablePayload_IsDiscarded_ByGlobalJsonExceptionPolicy(string data)
    {
        await using var provider = CreateServiceProvider(inbox: cfg => cfg.Policies.OnException<JsonException>().Discard());
        await AddAsync(provider, Inbox(PayloadType, data));

        await ProcessInboxAsync(provider);

        Assert.Empty(await InboxRowsAsync(provider));
    }

    [Theory]
    [InlineData(MalformedPayload)]
    [InlineData(NullPayload)]
    public async Task Inbox_UnreadablePayload_IsDiscarded_ByHandlerJsonExceptionPolicy(string data)
    {
        await using var provider = CreateServiceProvider(inbox: cfg => cfg
            .ForHandler<PayloadMessageHandler, PayloadMessage>()
            .OnException<JsonException>().Discard());
        await AddAsync(provider, Inbox(PayloadType, data));

        await ProcessInboxAsync(provider);

        Assert.Empty(await InboxRowsAsync(provider));
    }

    [Fact]
    public async Task Outbox_UnreadablePayload_WithoutPolicy_IsRetriedAndBlocksItsGroup()
    {
        await using var provider = CreateServiceProvider();
        var unreadable = Outbox(PayloadType, MalformedPayload);
        var behind = Outbox(PayloadType, """{"Id":2}""");
        await AddAsync(provider, unreadable, behind);

        await ProcessOutboxAsync(provider);

        var rows = await OutboxRowsAsync(provider);
        Assert.Equal(1, rows.Single(m => m.Id == unreadable.Id).RetryCount);
        Assert.Null(rows.Single(m => m.Id == behind.Id).CompletedAt);
    }

    [Fact]
    public async Task Inbox_UnreadablePayload_WithoutPolicy_IsRetriedAndBlocksItsGroup()
    {
        await using var provider = CreateServiceProvider();
        var unreadable = Inbox(PayloadType, MalformedPayload);
        var behind = Inbox(PayloadType, """{"Id":2}""");
        await AddAsync(provider, unreadable, behind);

        await ProcessInboxAsync(provider);

        var rows = await InboxRowsAsync(provider);
        Assert.Equal(1, rows.Single(m => m.Id == unreadable.Id).RetryCount);
        Assert.Null(rows.Single(m => m.Id == behind.Id).CompletedAt);
    }

    [Fact]
    public async Task Outbox_UnknownType_IsRetried_DespiteCatchAllPolicy()
    {
        await using var provider = CreateServiceProvider(outbox: cfg => cfg.Policies.OnException<Exception>().Discard());
        await AddAsync(provider, Outbox("Sample.NotYetDeployed", "{}"));

        await ProcessOutboxAsync(provider);

        Assert.Equal(1, Assert.Single(await OutboxRowsAsync(provider)).RetryCount);
    }

    [Fact]
    public async Task Inbox_UnknownType_IsRetried_DespiteCatchAllPolicy()
    {
        await using var provider = CreateServiceProvider(inbox: cfg => cfg.Policies.OnException<Exception>().Discard());
        await AddAsync(provider, Inbox("Sample.NotYetDeployed", "{}"));

        await ProcessInboxAsync(provider);

        Assert.Equal(1, Assert.Single(await InboxRowsAsync(provider)).RetryCount);
    }

    [Fact]
    public async Task Outbox_UnknownType_IsDiscarded_ByUnknownMessageTypePolicy()
    {
        await using var provider = CreateServiceProvider(outbox: cfg => cfg.Policies.OnException<UnknownMessageTypeException>().Discard());
        await AddAsync(provider, Outbox("Sample.Retired", "{}"));

        await ProcessOutboxAsync(provider);

        Assert.Empty(await OutboxRowsAsync(provider));
    }

    [Fact]
    public async Task Outbox_UnknownType_IsDiscarded_ByDeploymentGapPolicy()
    {
        await using var provider = CreateServiceProvider(outbox: cfg => cfg.Policies.OnException<DeploymentGapException>().Discard());
        await AddAsync(provider, Outbox("Sample.Retired", "{}"));

        await ProcessOutboxAsync(provider);

        Assert.Empty(await OutboxRowsAsync(provider));
    }

    [Fact]
    public async Task Inbox_UnknownType_IsDiscarded_ByUnknownMessageTypePolicy()
    {
        await using var provider = CreateServiceProvider(inbox: cfg => cfg.Policies.OnException<UnknownMessageTypeException>().Discard());
        await AddAsync(provider, Inbox("Sample.Retired", "{}"));

        await ProcessInboxAsync(provider);

        Assert.Empty(await InboxRowsAsync(provider));
    }

    [Fact]
    public async Task Outbox_UnbuildableHandler_IsRetried_DespiteCatchAllPolicies()
    {
        await using var provider = CreateServiceProvider(outbox: cfg =>
        {
            cfg.Policies.OnException<Exception>().Discard();
            cfg.ForHandler<UnbuildableMessageHandler, UnbuildableMessage>().OnException<Exception>().Discard();
        });
        await AddAsync(provider, new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new UnbuildableMessage(1)));

        await ProcessOutboxAsync(provider);

        Assert.Equal(1, Assert.Single(await OutboxRowsAsync(provider)).RetryCount);
    }

    [Fact]
    public async Task Inbox_UnbuildableHandler_IsRetried_DespiteCatchAllPolicies()
    {
        await using var provider = CreateServiceProvider(inbox: cfg =>
        {
            cfg.Policies.OnException<Exception>().Discard();
            cfg.ForHandler<UnbuildableMessageHandler, UnbuildableMessage>().OnException<Exception>().Discard();
        });
        await AddAsync(provider, new InboxMessage(Guid.NewGuid(), DateTime.UtcNow, new UnbuildableMessage(1)));

        await ProcessInboxAsync(provider);

        Assert.Equal(1, Assert.Single(await InboxRowsAsync(provider)).RetryCount);
    }

    [Fact]
    public async Task Outbox_UnbuildableHandler_IsDiscarded_ByHandlerDeploymentGapPolicy()
    {
        await using var provider = CreateServiceProvider(outbox: cfg => cfg
            .ForHandler<UnbuildableMessageHandler, UnbuildableMessage>()
            .OnException<HandlerResolutionException>().Discard());
        await AddAsync(provider, new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new UnbuildableMessage(1)));

        await ProcessOutboxAsync(provider);

        Assert.Empty(await OutboxRowsAsync(provider));
    }

    [Fact]
    public async Task Inbox_UnbuildableHandler_IsDiscarded_ByHandlerDeploymentGapPolicy()
    {
        await using var provider = CreateServiceProvider(inbox: cfg => cfg
            .ForHandler<UnbuildableMessageHandler, UnbuildableMessage>()
            .OnException<DeploymentGapException>().Discard());
        await AddAsync(provider, new InboxMessage(Guid.NewGuid(), DateTime.UtcNow, new UnbuildableMessage(1)));

        await ProcessInboxAsync(provider);

        Assert.Empty(await InboxRowsAsync(provider));
    }

    [Fact]
    public async Task UnbuildableHandler_FailsWithHandlerResolutionException_WrappingTheContainersError()
    {
        await using var provider = CreateServiceProvider(outbox: cfg => cfg.Policies.OnException<HandlerResolutionException>().MarkAsCompleted());
        await AddAsync(provider, new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new UnbuildableMessage(1)));

        await ProcessOutboxAsync(provider);

        var received = Assert.IsType<HandlerResolutionException>(ExceptionHandler(provider).ReceivedException);
        Assert.Equal(typeof(UnbuildableMessageHandler), received.HandlerType);
        Assert.IsType<InvalidOperationException>(received.InnerException);
    }

    [Fact]
    public async Task HandlerTimeout_ReachesTimeoutExceptionPolicy_AsHandlerTimeoutException()
    {
        await using var provider = CreateServiceProvider(outbox: cfg =>
        {
            cfg.HandlerTimeout = TimeSpan.FromMilliseconds(250);
            cfg.Policies.OnException<TimeoutException>().MarkAsCompleted();
        });
        BlockingMessageHandler.BlockingIds.Add(1);
        await AddAsync(provider, new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new BlockingMessage(1)));

        await ProcessOutboxAsync(provider);

        Assert.True(BlockingMessageHandler.WasCancelled, "the handler was never cancelled");
        Assert.IsType<HandlerTimeoutException>(ExceptionHandler(provider).ReceivedException);
        Assert.NotNull(Assert.Single(await OutboxRowsAsync(provider)).CompletedAt);
    }

    [Fact]
    public async Task Outbox_SaveFailureAfterTheHandler_ReachesItsPolicy()
    {
        await using var provider = CreateServiceProvider(outbox: cfg => cfg.Policies.OnException<DbUpdateException>().Discard());
        await AddAsync(provider, new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new UnsavableMessage(1)));

        await ProcessOutboxAsync(provider);

        Assert.Empty(await OutboxRowsAsync(provider));
    }

    [Fact]
    public async Task Inbox_SaveFailureAfterTheHandler_ReachesItsPolicy()
    {
        await using var provider = CreateServiceProvider(inbox: cfg => cfg.Policies.OnException<DbUpdateException>().Discard());
        await AddAsync(provider, new InboxMessage(Guid.NewGuid(), DateTime.UtcNow, new UnsavableMessage(1)));

        await ProcessInboxAsync(provider);

        Assert.Empty(await InboxRowsAsync(provider));
    }

    [Fact]
    public async Task CustomExceptionHandler_ReceivesTheCause()
    {
        await using var provider = CreateServiceProvider(outbox: cfg => cfg.Policies.OnException<DataException>().MarkAsCompleted());
        await AddAsync(provider, new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new DiscardMessage(1)));

        await ProcessOutboxAsync(provider);

        Assert.IsType<DataException>(ExceptionHandler(provider).ReceivedException);
    }

    private ServiceProvider CreateServiceProvider(
        Action<OutboxServiceConfiguration>? outbox = null,
        Action<InboxServiceConfiguration>? inbox = null)
    {
        var services = new ServiceCollection();
        var loggerFactory = LoggerFactory.Create(builder => builder.ConfigureTestLogger(_testOutputHelper));

        services.AddLogging(builder => builder.ConfigureTestLogger(_testOutputHelper));
        services.AddDbContext<InboxOutboxDbContext>(options => options
            .UseNpgsql(Database.ConnectionString)
            .UseLoggerFactory(loggerFactory));
        services.AddOutboxServices<InboxOutboxDbContext>(cfg => outbox?.Invoke(cfg));
        services.AddInboxServices<InboxOutboxDbContext>(cfg => inbox?.Invoke(cfg));
        services.AddUndergroundOutboxTestMessageHandlers();
        services.AddSingleton<MarkAsCompletedExceptionHandler<OutboxMessage>>();

        return services.BuildServiceProvider();
    }

    private static MarkAsCompletedExceptionHandler<OutboxMessage> ExceptionHandler(IServiceProvider provider)
        => provider.GetRequiredService<MarkAsCompletedExceptionHandler<OutboxMessage>>();

    private static OutboxMessage Outbox(string type, string data) => new(Guid.NewGuid(), DateTime.UtcNow, type, data);

    private static InboxMessage Inbox(string type, string data) => new(Guid.NewGuid(), DateTime.UtcNow, type, data);

    private static async Task AddAsync(IServiceProvider provider, params OutboxMessage[] messages)
    {
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<InboxOutboxDbContext>();

        await context.ExecuteInTransactionAsync(
            ct => scope.ServiceProvider.GetRequiredService<IOutbox>().AddMessagesAsync(context, messages, ct),
            TestContext.Current.CancellationToken);
    }

    private static async Task AddAsync(IServiceProvider provider, params InboxMessage[] messages)
    {
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<InboxOutboxDbContext>();

        await context.ExecuteInTransactionAsync(
            ct => scope.ServiceProvider.GetRequiredService<IInbox>().AddMessagesAsync(context, messages, ct),
            TestContext.Current.CancellationToken);
    }

    private static Task ProcessOutboxAsync(IServiceProvider provider)
        => provider.GetRequiredService<ConcurrentProcessor<OutboxMessage>>().ProcessUntilIdleAsync(TestContext.Current.CancellationToken);

    private static Task ProcessInboxAsync(IServiceProvider provider)
        => provider.GetRequiredService<ConcurrentProcessor<InboxMessage>>().ProcessUntilIdleAsync(TestContext.Current.CancellationToken);

    private static async Task<List<OutboxMessage>> OutboxRowsAsync(IServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<InboxOutboxDbContext>();

        return await context.OutboxMessages.AsNoTracking().ToListAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<List<InboxMessage>> InboxRowsAsync(IServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<InboxOutboxDbContext>();

        return await context.InboxMessages.AsNoTracking().ToListAsync(TestContext.Current.CancellationToken);
    }
}
