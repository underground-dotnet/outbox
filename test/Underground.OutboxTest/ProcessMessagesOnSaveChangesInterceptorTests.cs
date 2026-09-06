using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

using Underground.Outbox;
using Underground.Outbox.Configuration;
using Underground.Outbox.Data;
using Underground.OutboxTest.TestHandler;

namespace Underground.OutboxTest;

// this class drives a hosted service against the same static collections on ExampleMessageHandler that
// the Domain tests assert on, so it has to run in the collection that serializes them
[Collection("ExampleMessageHandler Collection")]
public class ProcessMessagesOnSaveChangesInterceptorTests : DatabaseTest
{
    private readonly ITestOutputHelper _testOutputHelper;
    private readonly IServiceProvider _serviceProvider;

    public ProcessMessagesOnSaveChangesInterceptorTests(ITestOutputHelper testOutputHelper) : base(testOutputHelper)
    {
        Container.StartAsync(CancellationToken.None).GetAwaiter().GetResult();

        _testOutputHelper = testOutputHelper;
        ExampleMessageHandler.CalledWith.Clear();
        ExampleMessageHandler.ObjectIds.Clear();

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddBaseServices(Container, _testOutputHelper);

        serviceCollection.AddOutboxServices<TestDbContext>(cfg =>
        {
            cfg.AddHandler<ExampleMessageHandler, ExampleMessage>();
        });

        _serviceProvider = serviceCollection.BuildServiceProvider();
    }

    private async Task RunBackgroundServiceAsync(CancellationToken cancellationToken)
    {
        var services = _serviceProvider.GetRequiredService<IEnumerable<IHostedService>>();
        foreach (var service in services)
        {
            await service.StartAsync(cancellationToken);
        }
    }

    private async Task StopBackgroundServiceAsync(CancellationToken cancellationToken)
    {
        var services = _serviceProvider.GetRequiredService<IEnumerable<IHostedService>>();
        foreach (var service in services)
        {
            await service.StopAsync(cancellationToken);
        }
    }

    [Fact]
    public async Task SaveChanges_TriggersOutboxProcessing_WhenNewOutboxMessagesWereAdded()
    {
        // Arrange
        var context = CreateDbContext(_serviceProvider.GetRequiredService<ProcessMessagesOnSaveChangesInterceptor>());
        var outbox = _serviceProvider.GetRequiredService<IOutbox>();
        var msg1 = new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new ExampleMessage(10));
        await RunBackgroundServiceAsync(TestContext.Current.CancellationToken);

        // Act
        await using (var transaction = await context.Database.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            await outbox.AddMessageAsync(context, msg1, TestContext.Current.CancellationToken);
            await transaction.CommitAsync(TestContext.Current.CancellationToken);
        }

        // Assert
        SpinWait.SpinUntil(() => !ExampleMessageHandler.ObjectIds.IsEmpty, TimeSpan.FromSeconds(10));
        Assert.Single(ExampleMessageHandler.ObjectIds);
        await StopBackgroundServiceAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// A context and an interceptor wired to recorders rather than to the real processors, so a test reads
    /// the notifications a commit produced instead of waiting for a background service to act on them.
    /// </summary>
    private async Task<(InboxOutboxDbContext Context, RecordingProcessor Outbox, RecordingProcessor Inbox)> CreateRecordingContextAsync(CancellationToken cancellationToken)
    {
        var outbox = new RecordingProcessor();
        var inbox = new RecordingProcessor();

        var services = new ServiceCollection();
        services.AddSingleton<IOutbox>(outbox);
        services.AddSingleton<IInbox>(inbox);

        var interceptor = new ProcessMessagesOnSaveChangesInterceptor(
            services.BuildServiceProvider(),
            NullLogger<ProcessMessagesOnSaveChangesInterceptor>.Instance);

        var options = new DbContextOptionsBuilder<InboxOutboxDbContext>()
            .UseNpgsql(Container.GetConnectionString())
            .AddInterceptors(interceptor)
            .Options;

        var context = new InboxOutboxDbContext(options);
        await context.Database.EnsureCreatedAsync(cancellationToken);

        return (context, outbox, inbox);
    }

    private static OutboxMessage NewOutboxMessage() => new(Guid.NewGuid(), DateTime.UtcNow, new ExampleMessage(10));

    private static InboxMessage NewInboxMessage() => new(Guid.NewGuid(), DateTime.UtcNow, new ExampleMessage(10));

    [Fact]
    public async Task Commit_DoesNotTriggerProcessing_WhenAnEarlierTransactionWasRolledBack()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (context, outbox, inbox) = await CreateRecordingContextAsync(cancellationToken);
        await using var _ = context.ConfigureAwait(false);

        var rolledBack = await context.Database.BeginTransactionAsync(cancellationToken);
        await using (rolledBack.ConfigureAwait(false))
        {
            context.OutboxMessages.Add(NewOutboxMessage());
            await context.SaveChangesAsync(cancellationToken);
            await rolledBack.RollbackAsync(cancellationToken);
        }

        // Act
        var committed = await context.Database.BeginTransactionAsync(cancellationToken);
        await using (committed.ConfigureAwait(false))
        {
            context.Users.Add(new User { Name = "no messages" });
            await context.SaveChangesAsync(cancellationToken);
            await committed.CommitAsync(cancellationToken);
        }

        // Assert
        Assert.Equal(0, outbox.ProcessMessagesCalls);
        Assert.Equal(0, inbox.ProcessMessagesCalls);
    }

    [Fact]
    public async Task Commit_TriggersBothProcessors_WhenSeparateSavesInTheTransactionStagedInboxAndOutbox()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (context, outbox, inbox) = await CreateRecordingContextAsync(cancellationToken);
        await using var _ = context.ConfigureAwait(false);

        // Act
        var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await using (transaction.ConfigureAwait(false))
        {
            context.OutboxMessages.Add(NewOutboxMessage());
            await context.SaveChangesAsync(cancellationToken);

            context.InboxMessages.Add(NewInboxMessage());
            await context.SaveChangesAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }

        // Assert
        Assert.Equal(1, outbox.ProcessMessagesCalls);
        Assert.Equal(1, inbox.ProcessMessagesCalls);
    }

    [Fact]
    public async Task Commit_TriggersProcessingOnce_WhenALaterSaveInTheTransactionStagedNoMessages()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (context, outbox, inbox) = await CreateRecordingContextAsync(cancellationToken);
        await using var _ = context.ConfigureAwait(false);

        // Act
        var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await using (transaction.ConfigureAwait(false))
        {
            context.OutboxMessages.Add(NewOutboxMessage());
            await context.SaveChangesAsync(cancellationToken);

            context.Users.Add(new User { Name = "no messages" });
            await context.SaveChangesAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }

        // Assert
        Assert.Equal(1, outbox.ProcessMessagesCalls);
        Assert.Equal(0, inbox.ProcessMessagesCalls);
    }

    /// <summary>
    /// Counts the notifications the interceptor sends. Adding is not reachable through it, so it is refused
    /// rather than left as a silent no-op.
    /// </summary>
    private sealed class RecordingProcessor : IOutbox, IInbox
    {
        public int ProcessMessagesCalls { get; private set; }

        public void ProcessMessages() => ProcessMessagesCalls++;

        public Task AddMessageAsync(IOutboxDbContext context, OutboxMessage message, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task AddMessagesAsync(IOutboxDbContext context, IEnumerable<OutboxMessage> messages, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task AddMessageAsync(IInboxDbContext context, InboxMessage message, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task AddMessagesAsync(IInboxDbContext context, IEnumerable<InboxMessage> messages, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
