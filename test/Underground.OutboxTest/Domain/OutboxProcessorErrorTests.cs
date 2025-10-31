using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Underground.Outbox;
using Underground.Outbox.Configuration;
using Underground.Outbox.Data;
using Underground.OutboxTest.TestHandler;

namespace Underground.OutboxTest.Domain;

[Collection("ExampleMessageHandler Collection")]
public class OutboxProcessorErrorTests : DatabaseTest
{
    private readonly ITestOutputHelper _testOutputHelper;

    public OutboxProcessorErrorTests(ITestOutputHelper testOutputHelper) : base(testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;

        ExampleMessageHandler.CalledWith.Clear();
        ExampleMessageHandler.ObjectIds.Clear();
        FailedMessageHandler.CalledWith.Clear();
        SecondMessageHandler.CalledWith.Clear();
        UserMessageHandler.CalledWithTransaction = null;
        CustomSqlMessageHandler.CalledWith.Clear();
        DiscardFailedMessageHandler.CalledWith.Clear();
    }

    [Fact]
    public async Task StopProcessingMessagesOnError()
    {
        // Arrange
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddBaseServices(Container, _testOutputHelper);

        serviceCollection.AddOutboxServices<TestDbContext>(cfg =>
        {
            cfg.AddHandler<FailedMessageHandler>();
            cfg.AddHandler<SecondMessageHandler>();
        });

        var serviceProvider = serviceCollection.BuildServiceProvider();
        var context = CreateDbContext();
        var msg = new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new ExampleMessage(10));
        var msg2 = new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new SecondMessage(11));
        var outbox = serviceProvider.GetRequiredService<IOutbox>();

        // Act
        await using (var transaction = await context.Database.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            await outbox.AddMessageAsync(context, msg, TestContext.Current.CancellationToken);
            await outbox.AddMessageAsync(context, msg2, TestContext.Current.CancellationToken);
            await transaction.CommitAsync(TestContext.Current.CancellationToken);
        }
        outbox.ProcessMessages();

        // Assert
        // Second message handler should not be called due to error in first message handler
        SpinWait.SpinUntil(() => FailedMessageHandler.CalledWith.Count > 0, TimeSpan.FromSeconds(3));
        // wait a bit to ensure second handler is not called
        await Task.Delay(1_000, TestContext.Current.CancellationToken);
        Assert.Empty(SecondMessageHandler.CalledWith);
    }

    [Fact]
    public async Task MarkSuccessfulMessagesAsProcessed()
    {
        // Arrange
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddBaseServices(Container, _testOutputHelper);

        serviceCollection.AddOutboxServices<TestDbContext>(cfg =>
        {
            cfg.AddHandler<ExampleMessageHandler>();
            cfg.AddHandler<SecondMessageHandler>();
        });

        var serviceProvider = serviceCollection.BuildServiceProvider();
        var context = CreateDbContext();
        var msg = new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new ExampleMessage(10));
        var msg2 = new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new SecondMessage(11));
        var outbox = serviceProvider.GetRequiredService<IOutbox>();

        // Act
        await using (var transaction = await context.Database.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            await outbox.AddMessageAsync(context, msg, TestContext.Current.CancellationToken);
            await outbox.AddMessageAsync(context, msg2, TestContext.Current.CancellationToken);
            await transaction.CommitAsync(TestContext.Current.CancellationToken);
        }
        outbox.ProcessMessages();

        // Assert
        SpinWait.SpinUntil(() => SecondMessageHandler.CalledWith.Count > 0, TimeSpan.FromSeconds(3));
        // TODO: replace with post commit hook
        // wait until transaction commits
        await Task.Delay(500, TestContext.Current.CancellationToken);
        var completed = await context.Database
            .SqlQuery<int>($"SELECT COUNT(id) AS \"Value\" FROM public.outbox WHERE processed_at IS NOT NULL")
            .SingleAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(2, completed);
    }

    [Fact]
    public async Task IncrementRetryCountForFailedMessage()
    {
        // Arrange
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddBaseServices(Container, _testOutputHelper);

        serviceCollection.AddOutboxServices<TestDbContext>(cfg =>
        {
            cfg.AddHandler<FailedMessageHandler>();
            cfg.AddHandler<SecondMessageHandler>();
        });

        var serviceProvider = serviceCollection.BuildServiceProvider();
        var context = CreateDbContext();
        var msg = new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new ExampleMessage(10));
        var msg2 = new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new SecondMessage(11));
        var outbox = serviceProvider.GetRequiredService<IOutbox>();

        // Act
        await using (var transaction = await context.Database.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            await outbox.AddMessageAsync(context, msg, TestContext.Current.CancellationToken);
            await outbox.AddMessageAsync(context, msg2, TestContext.Current.CancellationToken);
            await transaction.CommitAsync(TestContext.Current.CancellationToken);
        }
        outbox.ProcessMessages();

        // Assert
        SpinWait.SpinUntil(() => SecondMessageHandler.CalledWith.Count > 0, TimeSpan.FromSeconds(3));
        // If one message fails to process all previous successful messages should be rolled back (all or nothing)
        var completed = await context.Database
            .SqlQuery<int>($"SELECT COUNT(id) AS \"Value\" FROM public.outbox WHERE processed_at IS NOT NULL")
            .SingleAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(0, completed);

        // one message failed and retry count is incremented
        var notCompleted = await context.Database
        .SqlQuery<int>($"SELECT COUNT(id) AS \"Value\" FROM public.outbox WHERE retry_count > 0")
        .SingleAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(1, notCompleted);
    }

    [Fact]
    public async Task RollbackHandlerDbChangesOnError()
    {
        // Arrange
        var context = CreateDbContext();

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddBaseServices(Container, _testOutputHelper);

        serviceCollection.AddOutboxServices<TestDbContext>(cfg =>
        {
            cfg.AddHandler<UserMessageHandler>();
        });

        var serviceProvider = serviceCollection.BuildServiceProvider();
        var msg = new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new ExampleMessage(10));
        var outbox = serviceProvider.GetRequiredService<IOutbox>();

        await using (var transaction = await context.Database.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            await outbox.AddMessageAsync(context, msg, TestContext.Current.CancellationToken);
            await transaction.CommitAsync(TestContext.Current.CancellationToken);
        }

        // Act
        outbox.ProcessMessages();

        // Assert
        SpinWait.SpinUntil(() => UserMessageHandler.CalledWithTransaction != null, TimeSpan.FromSeconds(3));
        Assert.Empty(await context.Users.AsNoTracking().ToListAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RollbackHandlerCustomSqlChangesOnError()
    {
        // Arrange
        var context = CreateDbContext();

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddBaseServices(Container, _testOutputHelper);

        serviceCollection.AddOutboxServices<TestDbContext>(cfg =>
        {
            // tries to insert a user via raw SQL
            cfg.AddHandler<CustomSqlMessageHandler>();
        });

        var serviceProvider = serviceCollection.BuildServiceProvider();
        var msg = new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new ExampleMessage(10));
        var outbox = serviceProvider.GetRequiredService<IOutbox>();

        await using (var transaction = await context.Database.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            await outbox.AddMessageAsync(context, msg, TestContext.Current.CancellationToken);
            await transaction.CommitAsync(TestContext.Current.CancellationToken);
        }

        // Act
        outbox.ProcessMessages();

        // Assert
        SpinWait.SpinUntil(() => CustomSqlMessageHandler.CalledWith.Count > 0, TimeSpan.FromSeconds(3));
        Assert.Empty(await context.Users.AsNoTracking().ToListAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task OutboxTransactionIsUsedByInjectedDbContext()
    {
        // Arrange
        var context = CreateDbContext();

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddBaseServices(Container, _testOutputHelper);

        serviceCollection.AddOutboxServices<TestDbContext>(cfg =>
        {
            cfg.AddHandler<UserMessageHandler>();
        });

        var serviceProvider = serviceCollection.BuildServiceProvider();
        var msg = new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new ExampleMessage(10));
        var outbox = serviceProvider.GetRequiredService<IOutbox>();

        await using (var transaction = await context.Database.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            await outbox.AddMessageAsync(context, msg, TestContext.Current.CancellationToken);
            await transaction.CommitAsync(TestContext.Current.CancellationToken);
        }

        // Act
        outbox.ProcessMessages();

        // Assert
        SpinWait.SpinUntil(() => UserMessageHandler.CalledWithTransaction != null, TimeSpan.FromSeconds(3));
        Assert.NotNull(UserMessageHandler.CalledWithTransaction);
    }

    [Fact]
    public async Task DiscardMessagesOnSpecificException()
    {
        // Arrange
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddBaseServices(Container, _testOutputHelper);

        serviceCollection.AddOutboxServices<TestDbContext>(cfg =>
        {
            cfg.AddHandler<DiscardFailedMessageHandler>();
        });

        var serviceProvider = serviceCollection.BuildServiceProvider();
        var context = CreateDbContext();
        var msg = new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new ExampleMessage(10));
        var outbox = serviceProvider.GetRequiredService<IOutbox>();

        // Act
        await using (var transaction = await context.Database.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            await outbox.AddMessageAsync(context, msg, TestContext.Current.CancellationToken);
            await transaction.CommitAsync(TestContext.Current.CancellationToken);
        }
        outbox.ProcessMessages();

        // Assert
        SpinWait.SpinUntil(() => DiscardFailedMessageHandler.CalledWith.Count > 0, TimeSpan.FromSeconds(3));
        Assert.Empty(await context.OutboxMessages.AsNoTracking().ToListAsync(cancellationToken: TestContext.Current.CancellationToken));
    }
}
