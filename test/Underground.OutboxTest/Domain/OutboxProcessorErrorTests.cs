using System.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;


using Underground.Outbox;
using Underground.Outbox.Configuration;
using Underground.Outbox.Data;
using Underground.Outbox.Domain;

namespace Underground.OutboxTest.Domain;

public class OutboxProcessorErrorTests : DatabaseTest
{
    private readonly ITestOutputHelper _testOutputHelper;

    public OutboxProcessorErrorTests(ITestOutputHelper testOutputHelper) : base(testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;

        FailedMessageHandler.CalledWith.Clear();
        MockErrorHandler.CalledWithMessage = null;
        MockErrorHandler.CalledWithException = null;

        SecondMessageHandler.CalledWith.Clear();
    }

    [Fact]
    public async Task CallCommandErrorHandlerOnFailedMessage()
    {
        // Arrange
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddBaseServices(Container, _testOutputHelper);

        serviceCollection.AddOutboxServices(cfg =>
        {
            cfg.UseDbContext<TestDbContext>();
            cfg.AddHandler<FailedMessageHandler>();
        });

        var serviceProvider = serviceCollection.BuildServiceProvider();
        var context = CreateDbContext();
        var msg = new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new ExampleMessage(10));
        var outbox = serviceProvider.GetRequiredService<IOutbox>();

        // Act
        await using var transaction = await context.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
        await outbox.AddMessageAsync(context, msg);
        await transaction.CommitAsync(TestContext.Current.CancellationToken);
        await RunBackgroundServiceAsync(serviceProvider);

        // Assert
        // due to a race condition with starting the BackgroundService, we need to wait for the handler to be called
        var result = SpinWait.SpinUntil(() => MockErrorHandler.CalledWithMessage != null, TimeSpan.FromSeconds(3));
        Assert.True(result, "Error handler should be called on failed message");
        Assert.Equal(msg.Data, MockErrorHandler.CalledWithMessage?.Data);
        Assert.IsType<DataException>(MockErrorHandler.CalledWithException);
        await StopBackgroundServiceAsync(serviceProvider);
    }

    [Fact]
    public async Task ContinueProcessingMessagesOnError()
    {
        // Arrange
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddBaseServices(Container, _testOutputHelper);

        serviceCollection.AddOutboxServices(cfg =>
        {
            cfg.UseDbContext<TestDbContext>();
            cfg.AddHandler<FailedMessageHandler>();
            cfg.AddHandler<SecondMessageHandler>();
        });

        var serviceProvider = serviceCollection.BuildServiceProvider();
        var context = CreateDbContext();
        var msg = new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new ExampleMessage(10));
        var msg2 = new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new SecondMessage(11));
        var outbox = serviceProvider.GetRequiredService<IOutbox>();
        MockErrorHandler.StopProcessing = false; // Ensure we continue processing messages on error

        // Act
        await using var transaction = await context.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
        await outbox.AddMessageAsync(context, msg);
        await outbox.AddMessageAsync(context, msg2);
        await transaction.CommitAsync(TestContext.Current.CancellationToken);
        await RunBackgroundServiceAsync(serviceProvider);

        // Assert
        // due to a race condition with starting the BackgroundService, we need to wait for the handler to be called
        var result = SpinWait.SpinUntil(() => SecondMessageHandler.CalledWith.Count > 0, TimeSpan.FromSeconds(3));
        Assert.True(result, "Second message handler should be called despite error in first message handler");
        Assert.Equal(msg.Data, MockErrorHandler.CalledWithMessage?.Data);
        await StopBackgroundServiceAsync(serviceProvider);
    }

    [Fact]
    public async Task StopProcessingMessagesOnError()
    {
        // Arrange
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddBaseServices(Container, _testOutputHelper);

        serviceCollection.AddOutboxServices(cfg =>
        {
            cfg.UseDbContext<TestDbContext>();
            cfg.AddHandler<FailedMessageHandler>();
            cfg.AddHandler<SecondMessageHandler>();
        });

        var serviceProvider = serviceCollection.BuildServiceProvider();
        var context = CreateDbContext();
        var msg = new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new ExampleMessage(10));
        var msg2 = new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new SecondMessage(11));
        var outbox = serviceProvider.GetRequiredService<IOutbox>();
        MockErrorHandler.StopProcessing = true; // Ensure we stop processing messages on error

        var processor = serviceProvider.GetRequiredService<OutboxProcessor>();

        // Act
        await using var transaction = await context.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
        await outbox.AddMessageAsync(context, msg);
        await outbox.AddMessageAsync(context, msg2);
        await transaction.CommitAsync(TestContext.Current.CancellationToken);
        await processor.ProcessAsync(context, TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(SecondMessageHandler.CalledWith); // Second message handler should not be called due to error in first message handler
        Assert.Equal(msg.Data, MockErrorHandler.CalledWithMessage?.Data);
        await StopBackgroundServiceAsync(serviceProvider);
    }

    [Fact]
    public async Task MarkSuccessfulMessagesAsProcessed()
    {
        // Arrange
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddBaseServices(Container, _testOutputHelper);

        serviceCollection.AddOutboxServices(cfg =>
        {
            cfg.UseDbContext<TestDbContext>();
            cfg.AddHandler<FailedMessageHandler>();
            cfg.AddHandler<SecondMessageHandler>();
        });

        var serviceProvider = serviceCollection.BuildServiceProvider();
        var context = CreateDbContext();
        var msg = new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new ExampleMessage(10));
        var msg2 = new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new SecondMessage(11));
        var outbox = serviceProvider.GetRequiredService<IOutbox>();
        MockErrorHandler.StopProcessing = false; // Ensure we continue processing messages on error

        var processor = serviceProvider.GetRequiredService<OutboxProcessor>();

        // Act
        await using var transaction = await context.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
        await outbox.AddMessageAsync(context, msg);
        await outbox.AddMessageAsync(context, msg2);
        await transaction.CommitAsync(TestContext.Current.CancellationToken);
        await processor.ProcessAsync(context, TestContext.Current.CancellationToken);

        // Assert
        var completed = await context.Database
            .SqlQuery<int>($"SELECT COUNT(id) AS \"Value\" FROM public.outbox WHERE completed = true")
            .SingleAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(1, completed);

        var notCompleted = await context.Database
        .SqlQuery<int>($"SELECT COUNT(id) AS \"Value\" FROM public.outbox WHERE completed = false")
        .SingleAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(1, notCompleted);
    }

    [Fact]
    public async Task RollbackHandlerDbChangesOnError()
    {
        // Arrange
        var context = CreateDbContext();
        // create Users table
        await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddBaseServices(Container, _testOutputHelper);

        serviceCollection.AddOutboxServices(cfg =>
        {
            cfg.UseDbContext<TestDbContext>();
            cfg.AddHandler<UserMessageHandler>();
        });

        var serviceProvider = serviceCollection.BuildServiceProvider();
        var msg = new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new ExampleMessage(10));
        var outbox = serviceProvider.GetRequiredService<IOutbox>();

        using (var transaction = await context.Database.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            await outbox.AddMessageAsync(context, msg);
            await transaction.CommitAsync(TestContext.Current.CancellationToken);
        }

        // Act
        var processor = serviceProvider.GetRequiredService<OutboxProcessor>();
        await processor.ProcessAsync(context, TestContext.Current.CancellationToken);

        // Assert
        // Assert.Empty(context.ChangeTracker.Entries<User>());
        Assert.Empty(await context.Users.AsNoTracking().ToListAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    private static async Task RunBackgroundServiceAsync(IServiceProvider serviceProvider)
    {
        var service = serviceProvider.GetRequiredService<IHostedService>();
        await service.StartAsync(CancellationToken.None);
    }

    private static async Task StopBackgroundServiceAsync(IServiceProvider serviceProvider)
    {
        var service = serviceProvider.GetRequiredService<IHostedService>();
        await service.StopAsync(CancellationToken.None);
    }
}
