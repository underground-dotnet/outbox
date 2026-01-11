using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Underground.Outbox;
using Underground.Outbox.Configuration;
using Underground.Outbox.Data;
using Underground.Outbox.Domain;
using Underground.OutboxTest.TestHandler;

namespace Underground.OutboxTest.Domain;

[Collection("ExampleMessageHandler Collection")]
public class ProcessorErrorTests : DatabaseTest
{
    private readonly ITestOutputHelper _testOutputHelper;

    public ProcessorErrorTests(ITestOutputHelper testOutputHelper) : base(testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;

        ExampleMessageHandler.CalledWith.Clear();
        ExampleMessageHandler.ObjectIds.Clear();
        FailedMessageHandler.CalledWith.Clear();
        SecondMessageHandler.CalledWith.Clear();
        FailedUserMessageHandler.CalledWithTransaction = null;
    }

    [Fact]
    public async Task StopProcessingMessagesOnError()
    {
        // Arrange
        var serviceCollection = new ServiceCollection();

        serviceCollection.AddOutboxServices<TestDbContext>(cfg =>
        {
            cfg.AddHandler<FailedMessageHandler>();
            cfg.AddHandler<SecondMessageHandler>();
        });

        serviceCollection.AddBaseServices(Container, _testOutputHelper);
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
        await Processor<OutboxMessage>.ProcessWithDefaultValues(serviceProvider, TestContext.Current.CancellationToken);

        // Assert
        // Second message handler should not be called due to error in first message handler
        Assert.Empty(SecondMessageHandler.CalledWith);
    }

    [Fact]
    public async Task MarkSuccessfulMessagesAsProcessed()
    {
        // Arrange
        var serviceCollection = new ServiceCollection();

        serviceCollection.AddOutboxServices<TestDbContext>(cfg =>
        {
            cfg.AddHandler<ExampleMessageHandler>();
            cfg.AddHandler<SecondMessageHandler>();
        });

        serviceCollection.AddBaseServices(Container, _testOutputHelper);
        var serviceProvider = serviceCollection.BuildServiceProvider();
        var context = CreateDbContext();
        var msg = new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new ExampleMessage(10));
        var msg2 = new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new SecondMessage(11));
        var outbox = serviceProvider.GetRequiredService<IOutbox>();
        var processor = serviceProvider.GetRequiredService<SynchronousProcessor<OutboxMessage>>();

        // Act
        await using (var transaction = await context.Database.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            await outbox.AddMessageAsync(context, msg, TestContext.Current.CancellationToken);
            await outbox.AddMessageAsync(context, msg2, TestContext.Current.CancellationToken);
            await transaction.CommitAsync(TestContext.Current.CancellationToken);
        }
        await processor.ProcessAndWaitAsync(TestContext.Current.CancellationToken);

        // Assert
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

        serviceCollection.AddOutboxServices<TestDbContext>(cfg =>
        {
            cfg.AddHandler<FailedMessageHandler>();
            cfg.AddHandler<SecondMessageHandler>();
        });

        serviceCollection.AddBaseServices(Container, _testOutputHelper);
        var serviceProvider = serviceCollection.BuildServiceProvider();
        var context = CreateDbContext();
        var msg = new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new SecondMessage(10));
        var msg2 = new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new ExampleMessage(11));
        var outbox = serviceProvider.GetRequiredService<IOutbox>();

        // Act
        await using (var transaction = await context.Database.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            await outbox.AddMessageAsync(context, msg, TestContext.Current.CancellationToken);
            await outbox.AddMessageAsync(context, msg2, TestContext.Current.CancellationToken);
            await transaction.CommitAsync(TestContext.Current.CancellationToken);
        }
        await Processor<OutboxMessage>.ProcessWithDefaultValues(serviceProvider, TestContext.Current.CancellationToken);

        // Assert
        // First message of type SecondMessage should be processed successfully, the message afterwards failed
        var completed = await context.Database
            .SqlQuery<int>($"SELECT COUNT(id) AS \"Value\" FROM public.outbox WHERE processed_at IS NOT NULL AND retry_count = 0 AND id = {msg.Id}")
            .SingleAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(1, completed);

        // second message failed and retry count is incremented
        var notCompleted = await context.Database
        .SqlQuery<int>($"SELECT COUNT(id) AS \"Value\" FROM public.outbox WHERE processed_at IS NULL AND retry_count > 0 AND id = {msg2.Id}")
        .SingleAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(1, notCompleted);
    }

    [Fact]
    public async Task RollbackHandlerDbChangesOnError()
    {
        // Arrange
        var context = CreateDbContext();

        var serviceCollection = new ServiceCollection();

        serviceCollection.AddOutboxServices<TestDbContext>(cfg =>
        {
            cfg.AddHandler<FailedUserMessageHandler>();
        });

        serviceCollection.AddBaseServices(Container, _testOutputHelper);
        var serviceProvider = serviceCollection.BuildServiceProvider();
        var msg = new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new ExampleMessage(10));
        var outbox = serviceProvider.GetRequiredService<IOutbox>();

        await using (var transaction = await context.Database.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            await outbox.AddMessageAsync(context, msg, TestContext.Current.CancellationToken);
            await transaction.CommitAsync(TestContext.Current.CancellationToken);
        }

        // Act
        await Processor<OutboxMessage>.ProcessWithDefaultValues(serviceProvider, TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(await context.Users.AsNoTracking().ToListAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RollbackHandlerCustomSqlChangesOnError()
    {
        // Arrange
        var context = CreateDbContext();

        var serviceCollection = new ServiceCollection();

        serviceCollection.AddOutboxServices<TestDbContext>(cfg =>
        {
            // tries to insert a user via raw SQL
            cfg.AddHandler<CustomSqlMessageHandler>();
        });

        serviceCollection.AddBaseServices(Container, _testOutputHelper);
        var serviceProvider = serviceCollection.BuildServiceProvider();
        var msg = new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new ExampleMessage(10));
        var outbox = serviceProvider.GetRequiredService<IOutbox>();

        await using (var transaction = await context.Database.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            await outbox.AddMessageAsync(context, msg, TestContext.Current.CancellationToken);
            await transaction.CommitAsync(TestContext.Current.CancellationToken);
        }

        // Act
        await Processor<OutboxMessage>.ProcessWithDefaultValues(serviceProvider, TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(await context.Users.AsNoTracking().ToListAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task KeepDbChangesFromSuccessfullMessagesOnFailure()
    {
        // Arrange
        var context = CreateDbContext();

        var serviceCollection = new ServiceCollection();

        serviceCollection.AddOutboxServices<TestDbContext>(cfg =>
        {
            cfg.AddHandler<UserMessageHandler>();
            cfg.AddHandler<FailedUserMessageHandler>();
        });

        serviceCollection.AddBaseServices(Container, _testOutputHelper);
        var serviceProvider = serviceCollection.BuildServiceProvider();
        var msg = new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new SecondMessage(10));
        var msg2 = new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new ExampleMessage(11));
        var outbox = serviceProvider.GetRequiredService<IOutbox>();

        await using (var transaction = await context.Database.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            // first message processing is successful and will insert a new user
            await outbox.AddMessageAsync(context, msg, TestContext.Current.CancellationToken);
            // second message processing fails and the second inserted user should be rolled back
            await outbox.AddMessageAsync(context, msg2, TestContext.Current.CancellationToken);
            await transaction.CommitAsync(TestContext.Current.CancellationToken);
        }

        // Act
        await Processor<OutboxMessage>.ProcessWithDefaultValues(serviceProvider, TestContext.Current.CancellationToken);

        // Assert
        var completed = await context.Database
            .SqlQuery<int>($"SELECT COUNT(id) AS \"Value\" FROM public.users")
            .SingleAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(1, completed);

        var users = await context.Users.AsNoTracking().ToListAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Single(users);
        Assert.Equal("Testuser Success", users[0].Name);
    }

    [Fact]
    public async Task OutboxTransactionIsUsedByInjectedDbContext()
    {
        // Arrange
        var context = CreateDbContext();

        var serviceCollection = new ServiceCollection();

        serviceCollection.AddOutboxServices<TestDbContext>(cfg =>
        {
            cfg.AddHandler<FailedUserMessageHandler>();
        });

        serviceCollection.AddBaseServices(Container, _testOutputHelper);
        var serviceProvider = serviceCollection.BuildServiceProvider();
        var msg = new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new ExampleMessage(10));
        var outbox = serviceProvider.GetRequiredService<IOutbox>();

        await using (var transaction = await context.Database.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            await outbox.AddMessageAsync(context, msg, TestContext.Current.CancellationToken);
            await transaction.CommitAsync(TestContext.Current.CancellationToken);
        }

        // Act
        await Processor<OutboxMessage>.ProcessWithDefaultValues(serviceProvider, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(FailedUserMessageHandler.CalledWithTransaction);
    }

    [Fact]
    public async Task DiscardMessagesOnSpecificException()
    {
        // Arrange
        var serviceCollection = new ServiceCollection();

        serviceCollection.AddOutboxServices<TestDbContext>(cfg =>
        {
            cfg.AddHandler<DiscardFailedMessageHandler>();
        });

        serviceCollection.AddBaseServices(Container, _testOutputHelper);
        var serviceProvider = serviceCollection.BuildServiceProvider();
        var context = CreateDbContext();
        var msg = new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new ExampleMessage(10));
        var outbox = serviceProvider.GetRequiredService<IOutbox>();
        var processor = serviceProvider.GetRequiredService<SynchronousProcessor<OutboxMessage>>();

        // Act
        await using (var transaction = await context.Database.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            await outbox.AddMessageAsync(context, msg, TestContext.Current.CancellationToken);
            await transaction.CommitAsync(TestContext.Current.CancellationToken);
        }
        await processor.ProcessAndWaitAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(await context.OutboxMessages.AsNoTracking().ToListAsync(cancellationToken: TestContext.Current.CancellationToken));
    }
}
