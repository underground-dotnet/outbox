using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using System.Data;

using Underground.Outbox;
using Underground.Outbox.Configuration;
using Underground.Outbox.Data;
using Underground.Outbox.Domain;
using Underground.OutboxTest.TestHandler;
using Underground.OutboxTest.TestPolicies;

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
        FailedUserMessageHandler.Reset();
    }

    [Fact]
    public async Task StopProcessingMessagesOnError()
    {
        // Arrange
        var serviceCollection = new ServiceCollection();

        serviceCollection.AddOutboxServices<TestDbContext>(cfg =>
        {
            cfg.AddHandler<FailedMessageHandler, FailedMessage>();
            cfg.AddHandler<SecondMessageHandler, SecondMessage>();
        });

        serviceCollection.AddBaseServices(Container, _testOutputHelper);
        var serviceProvider = serviceCollection.BuildServiceProvider();
        var context = CreateDbContext();
        var msg = new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new FailedMessage(10));
        var msg2 = new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new SecondMessage(11));
        var outbox = serviceProvider.GetRequiredService<IOutbox>();

        // Act
        await using (var transaction = await context.Database.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            await outbox.AddMessageAsync(context, msg, TestContext.Current.CancellationToken);
            await outbox.AddMessageAsync(context, msg2, TestContext.Current.CancellationToken);
            await transaction.CommitAsync(TestContext.Current.CancellationToken);
        }
        await IProcessor<OutboxMessage>.ProcessWithDefaultValues(serviceProvider, TestContext.Current.CancellationToken);

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
            cfg.AddHandler<ExampleMessageHandler, ExampleMessage>();
            cfg.AddHandler<SecondMessageHandler, SecondMessage>();
        });

        serviceCollection.AddBaseServices(Container, _testOutputHelper);
        var serviceProvider = serviceCollection.BuildServiceProvider();
        var context = CreateDbContext();
        var msg = new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new ExampleMessage(10));
        var msg2 = new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new SecondMessage(11));
        var outbox = serviceProvider.GetRequiredService<IOutbox>();
        var processor = serviceProvider.GetRequiredService<ConcurrentProcessor<OutboxMessage>>();

        // Act
        await using (var transaction = await context.Database.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            await outbox.AddMessageAsync(context, msg, TestContext.Current.CancellationToken);
            await outbox.AddMessageAsync(context, msg2, TestContext.Current.CancellationToken);
            await transaction.CommitAsync(TestContext.Current.CancellationToken);
        }
        await processor.ProcessUntilIdleAsync(TestContext.Current.CancellationToken);

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
            cfg.AddHandler<FailedMessageHandler, FailedMessage>();
            cfg.AddHandler<SecondMessageHandler, SecondMessage>();
        });

        serviceCollection.AddBaseServices(Container, _testOutputHelper);
        var serviceProvider = serviceCollection.BuildServiceProvider();
        var context = CreateDbContext();
        var msg = new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new SecondMessage(10));
        var msg2 = new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new FailedMessage(11));
        var outbox = serviceProvider.GetRequiredService<IOutbox>();

        // Act
        await using (var transaction = await context.Database.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            await outbox.AddMessageAsync(context, msg, TestContext.Current.CancellationToken);
            await outbox.AddMessageAsync(context, msg2, TestContext.Current.CancellationToken);
            await transaction.CommitAsync(TestContext.Current.CancellationToken);
        }
        await IProcessor<OutboxMessage>.ProcessWithDefaultValues(serviceProvider, TestContext.Current.CancellationToken);
        // a Group offers one message per claim, so the failing message behind the first one needs a second
        await IProcessor<OutboxMessage>.ProcessWithDefaultValues(serviceProvider, TestContext.Current.CancellationToken);

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

    /// <summary>
    /// An outbox Handler dispatches with no transaction open, so whatever it wrote to this database is
    /// already committed by the time it throws. There is nothing to roll it back to, and that is the
    /// bargain of the Lease: short transactions, at-least-once delivery, and an outbox Handler whose
    /// business is an effect outside this database.
    /// </summary>
    [Fact]
    public async Task HandlerDbChangesSurviveAnErrorBecauseTheOutboxHoldsNoTransaction()
    {
        // Arrange
        var context = CreateDbContext();

        var serviceCollection = new ServiceCollection();

        serviceCollection.AddOutboxServices<TestDbContext>(cfg =>
        {
            cfg.AddHandler<FailedUserMessageHandler, FailedUserMessage>();
        });

        serviceCollection.AddBaseServices(Container, _testOutputHelper);
        var serviceProvider = serviceCollection.BuildServiceProvider();
        var msg = new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new FailedUserMessage(10));
        var outbox = serviceProvider.GetRequiredService<IOutbox>();

        await using (var transaction = await context.Database.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            await outbox.AddMessageAsync(context, msg, TestContext.Current.CancellationToken);
            await transaction.CommitAsync(TestContext.Current.CancellationToken);
        }

        // Act
        await IProcessor<OutboxMessage>.ProcessWithDefaultValues(serviceProvider, TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(await context.Users.AsNoTracking().ToListAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// The same, for a Handler that writes through raw SQL rather than through the change tracker: neither
    /// route is inside a transaction on the outbox, so neither is undone.
    /// </summary>
    [Fact]
    public async Task HandlerCustomSqlChangesSurviveAnErrorBecauseTheOutboxHoldsNoTransaction()
    {
        // Arrange
        var context = CreateDbContext();

        var serviceCollection = new ServiceCollection();

        serviceCollection.AddOutboxServices<TestDbContext>(cfg =>
        {
            // tries to insert a user via raw SQL
            cfg.AddHandler<CustomSqlMessageHandler, CustomSqlMessage>();
        });

        serviceCollection.AddBaseServices(Container, _testOutputHelper);
        var serviceProvider = serviceCollection.BuildServiceProvider();
        var msg = new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new CustomSqlMessage(10));
        var outbox = serviceProvider.GetRequiredService<IOutbox>();

        await using (var transaction = await context.Database.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            await outbox.AddMessageAsync(context, msg, TestContext.Current.CancellationToken);
            await transaction.CommitAsync(TestContext.Current.CancellationToken);
        }

        // Act
        await IProcessor<OutboxMessage>.ProcessWithDefaultValues(serviceProvider, TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(await context.Users.AsNoTracking().ToListAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task KeepDbChangesFromSuccessfullMessagesOnFailure()
    {
        // Arrange
        var context = CreateDbContext();

        var serviceCollection = new ServiceCollection();

        serviceCollection.AddOutboxServices<TestDbContext>(cfg =>
        {
            cfg.AddHandler<UserMessageHandler, UserMessage>();
            cfg.AddHandler<FailedUserMessageHandler, FailedUserMessage>();
        });

        serviceCollection.AddBaseServices(Container, _testOutputHelper);
        var serviceProvider = serviceCollection.BuildServiceProvider();
        var msg = new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new UserMessage(10));
        var msg2 = new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new FailedUserMessage(11));
        var outbox = serviceProvider.GetRequiredService<IOutbox>();

        await using (var transaction = await context.Database.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            // first message processing is successful and will insert a new user
            await outbox.AddMessageAsync(context, msg, TestContext.Current.CancellationToken);
            // second message processing fails after inserting a user of its own, which the outbox has no
            // transaction to undo - what this test pins is that the first message's write is untouched by it
            await outbox.AddMessageAsync(context, msg2, TestContext.Current.CancellationToken);
            await transaction.CommitAsync(TestContext.Current.CancellationToken);
        }

        // Act
        await IProcessor<OutboxMessage>.ProcessWithDefaultValues(serviceProvider, TestContext.Current.CancellationToken);
        // a Group offers one message per claim, so the failing message behind the first one needs a second
        await IProcessor<OutboxMessage>.ProcessWithDefaultValues(serviceProvider, TestContext.Current.CancellationToken);

        // Assert
        var users = await context.Users.AsNoTracking().ToListAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains(users, u => string.Equals(u.Name, "Testuser Success", StringComparison.Ordinal));
    }

    /// <summary>
    /// The claim is committed before the Handler is entered, so a Handler's DbContext has no ambient
    /// transaction: nothing it does can hold the claim transaction open across a call to an external
    /// system, which is the whole point of the Lease.
    /// </summary>
    [Fact]
    public async Task OutboxHandlerRunsWithNoTransactionOpen()
    {
        // Arrange
        var context = CreateDbContext();

        var serviceCollection = new ServiceCollection();

        serviceCollection.AddOutboxServices<TestDbContext>(cfg =>
        {
            cfg.AddHandler<FailedUserMessageHandler, FailedUserMessage>();
        });

        serviceCollection.AddBaseServices(Container, _testOutputHelper);
        var serviceProvider = serviceCollection.BuildServiceProvider();
        var msg = new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new FailedUserMessage(10));
        var outbox = serviceProvider.GetRequiredService<IOutbox>();

        await using (var transaction = await context.Database.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            await outbox.AddMessageAsync(context, msg, TestContext.Current.CancellationToken);
            await transaction.CommitAsync(TestContext.Current.CancellationToken);
        }

        // Act
        await IProcessor<OutboxMessage>.ProcessWithDefaultValues(serviceProvider, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(FailedUserMessageHandler.WasCalled, "the handler never ran");
        Assert.Null(FailedUserMessageHandler.CalledWithTransaction);
    }

    [Fact]
    public async Task DiscardMessagesOnSpecificException()
    {
        // Arrange
        var serviceCollection = new ServiceCollection();

        serviceCollection.AddOutboxServices<TestDbContext>(cfg =>
        {
            cfg.AddHandler<DiscardFailedMessageHandler, DiscardMessage>()
                .OnException<DataException>()
                .Discard();
        });

        serviceCollection.AddBaseServices(Container, _testOutputHelper);
        var serviceProvider = serviceCollection.BuildServiceProvider();
        var context = CreateDbContext();
        var msg = new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new DiscardMessage(10));
        var outbox = serviceProvider.GetRequiredService<IOutbox>();
        var processor = serviceProvider.GetRequiredService<ConcurrentProcessor<OutboxMessage>>();

        // Act
        await using (var transaction = await context.Database.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            await outbox.AddMessageAsync(context, msg, TestContext.Current.CancellationToken);
            await transaction.CommitAsync(TestContext.Current.CancellationToken);
        }
        await processor.ProcessUntilIdleAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(await context.OutboxMessages.AsNoTracking().ToListAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DiscardMessagesOnGlobalExceptionPolicy()
    {
        // Arrange
        var serviceCollection = new ServiceCollection();

        serviceCollection.AddOutboxServices<TestDbContext>(cfg =>
        {
            cfg.Policies.OnException<DataException>().Discard();

            cfg.AddHandler<DiscardFailedMessageHandler, DiscardMessage>();
        });

        serviceCollection.AddBaseServices(Container, _testOutputHelper);
        var serviceProvider = serviceCollection.BuildServiceProvider();
        var context = CreateDbContext();
        var msg = new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new DiscardMessage(10));
        var outbox = serviceProvider.GetRequiredService<IOutbox>();
        var processor = serviceProvider.GetRequiredService<ConcurrentProcessor<OutboxMessage>>();

        // Act
        await using (var transaction = await context.Database.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            await outbox.AddMessageAsync(context, msg, TestContext.Current.CancellationToken);
            await transaction.CommitAsync(TestContext.Current.CancellationToken);
        }
        await processor.ProcessUntilIdleAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(await context.OutboxMessages.AsNoTracking().ToListAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task MessageHandlerPolicyOverwritesGlobalExceptionPolicy()
    {
        // Arrange
        var serviceCollection = new ServiceCollection();

        serviceCollection.AddOutboxServices<TestDbContext>(cfg =>
        {
            // global policy will delete it
            cfg.Policies.OnException<DataException>().Discard();

            // message handler policy should prevent deletion
            cfg.AddHandler<DiscardFailedMessageHandler, DiscardMessage>()
                .OnException<DataException>().MarkAsProcessed();
        });

        serviceCollection.AddBaseServices(Container, _testOutputHelper);
        serviceCollection.AddSingleton<MarkAsProcessedExceptionHandler<OutboxMessage>>();
        var serviceProvider = serviceCollection.BuildServiceProvider();
        var context = CreateDbContext();
        var msg = new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new DiscardMessage(10));
        var outbox = serviceProvider.GetRequiredService<IOutbox>();
        var processor = serviceProvider.GetRequiredService<ConcurrentProcessor<OutboxMessage>>();

        // Act
        await using (var transaction = await context.Database.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            await outbox.AddMessageAsync(context, msg, TestContext.Current.CancellationToken);
            await transaction.CommitAsync(TestContext.Current.CancellationToken);
        }
        await processor.ProcessUntilIdleAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(await context.OutboxMessages.AsNoTracking().ToListAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DuplicateExceptionPoliciesAreOnlyExecutedOnce()
    {
        // Arrange
        var serviceCollection = new ServiceCollection();

        serviceCollection.AddOutboxServices<TestDbContext>(cfg =>
        {
            cfg.Policies.OnException<DataException>().MarkAsProcessed();

            cfg.AddHandler<DiscardFailedMessageHandler, DiscardMessage>()
                .OnException<DataException>().MarkAsProcessed();
        });

        serviceCollection.AddBaseServices(Container, _testOutputHelper);
        serviceCollection.AddSingleton<MarkAsProcessedExceptionHandler<OutboxMessage>>();
        var serviceProvider = serviceCollection.BuildServiceProvider();
        var context = CreateDbContext();
        var msg = new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new DiscardMessage(10));
        var outbox = serviceProvider.GetRequiredService<IOutbox>();
        var processor = serviceProvider.GetRequiredService<ConcurrentProcessor<OutboxMessage>>();

        // Act
        await using (var transaction = await context.Database.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            await outbox.AddMessageAsync(context, msg, TestContext.Current.CancellationToken);
            await transaction.CommitAsync(TestContext.Current.CancellationToken);
        }
        await processor.ProcessUntilIdleAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, serviceProvider.GetRequiredService<MarkAsProcessedExceptionHandler<OutboxMessage>>().CallCount);
    }

    [Fact]
    public async Task ExceptionPolicyOnlyAppliesToConfiguredMessageTypeForMultiMessageHandler()
    {
        // Arrange
        var serviceCollection = new ServiceCollection();

        serviceCollection.AddOutboxServices<TestDbContext>(cfg =>
        {
            cfg.AddHandler<FailedMultipleMessagesHandler, FailedMultiMessageA>()
                .OnException<InvalidOperationException>()
                .Discard();
            cfg.AddHandler<FailedMultipleMessagesHandler, FailedMultiMessageB>();
        });

        serviceCollection.AddBaseServices(Container, _testOutputHelper);
        var serviceProvider = serviceCollection.BuildServiceProvider();
        var context = CreateDbContext();
        var msg = new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new FailedMultiMessageB(10));
        var outbox = serviceProvider.GetRequiredService<IOutbox>();
        var processor = serviceProvider.GetRequiredService<IProcessor<OutboxMessage>>();

        // Act
        await using (var transaction = await context.Database.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            await outbox.AddMessageAsync(context, msg, TestContext.Current.CancellationToken);
            await transaction.CommitAsync(TestContext.Current.CancellationToken);
        }
        await processor.ProcessHeadAsync(serviceProvider.CreateScope(), TestContext.Current.CancellationToken);

        // Assert
        var failedMessage = await context.OutboxMessages
            .AsNoTracking()
            .SingleAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(msg.Id, failedMessage.Id);
        Assert.Equal(1, failedMessage.RetryCount);
    }
}
