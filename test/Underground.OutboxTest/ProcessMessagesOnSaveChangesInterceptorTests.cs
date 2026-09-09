using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

using Underground.Outbox;
using Underground.Outbox.Configuration;
using Underground.Outbox.Data;
using Underground.Outbox.Domain;
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
        _testOutputHelper = testOutputHelper;
        ExampleMessageHandler.CalledWith.Clear();
        ExampleMessageHandler.ObjectIds.Clear();

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddBaseServices(Database, _testOutputHelper);

        serviceCollection.AddTestOutbox(cfg =>
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
        var context = CreateDbContext(_serviceProvider.GetRequiredService<ProcessMessagesOnSaveChangesInterceptor<TestDbContext>>());
        var outbox = _serviceProvider.GetRequiredService<IOutbox<TestDbContext>>();
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
    private (InboxOutboxDbContext Context, RecordingSignal Outbox, RecordingSignal Inbox) CreateRecordingContext()
    {
        var outbox = new RecordingSignal();
        var inbox = new RecordingSignal();

        var services = new ServiceCollection();
        services.AddSingleton<IWorkSignal<InboxOutboxDbContext, OutboxMessage>>(outbox);
        services.AddSingleton<IWorkSignal<InboxOutboxDbContext, InboxMessage>>(inbox);

        var interceptor = new ProcessMessagesOnSaveChangesInterceptor<InboxOutboxDbContext>(
            services.BuildServiceProvider(),
            NullLogger<ProcessMessagesOnSaveChangesInterceptor<InboxOutboxDbContext>>.Instance);

        var options = new DbContextOptionsBuilder<InboxOutboxDbContext>()
            .UseNpgsql(Database.ConnectionString)
            .AddInterceptors(interceptor)
            .Options;

        var context = new InboxOutboxDbContext(options);

        return (context, outbox, inbox);
    }

    private static OutboxMessage NewOutboxMessage() => new(Guid.NewGuid(), DateTime.UtcNow, new ExampleMessage(10));

    private static InboxMessage NewInboxMessage() => new(Guid.NewGuid(), DateTime.UtcNow, new ExampleMessage(10));

    [Fact]
    public async Task Commit_DoesNotTriggerProcessing_WhenAnEarlierTransactionWasRolledBack()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (context, outbox, inbox) = CreateRecordingContext();
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
        var (context, outbox, inbox) = CreateRecordingContext();
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
        var (context, outbox, inbox) = CreateRecordingContext();
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

    /// <summary>Counts the notifications the interceptor sends to one side of one context.</summary>
    private sealed class RecordingSignal : IWorkSignal<InboxOutboxDbContext, OutboxMessage>, IWorkSignal<InboxOutboxDbContext, InboxMessage>
    {
        public int ProcessMessagesCalls { get; private set; }

        public void ProcessMessages() => ProcessMessagesCalls++;
    }
}
