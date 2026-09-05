using Microsoft.Extensions.DependencyInjection;

using Underground.Outbox.Configuration;
using Underground.Outbox.Data;
using Underground.Outbox.Domain;
using Underground.OutboxTest.TestHandler;

namespace Underground.OutboxTest.Domain;

/// <summary>
/// Every message carries the instant from which it may be handled, and a failed attempt pushes that
/// instant into the future. Elapsed time is simulated by moving the stored instant into the past
/// rather than by waiting, so these tests are as fast as any other and cannot be flaky.
/// </summary>
[Collection("ExampleMessageHandler Collection")]
public class VisibleAtTests : DatabaseTest
{
    private readonly ITestOutputHelper _testOutputHelper;

    public VisibleAtTests(ITestOutputHelper testOutputHelper) : base(testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;

        ExampleMessageHandler.CalledWith.Clear();
        ExampleMessageHandler.ObjectIds.Clear();
        FailedMessageHandler.CalledWith.Clear();
    }

    [Fact]
    public async Task NewMessageIsVisibleFromTheMomentItIsWritten()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var serviceProvider = BuildServiceProvider(cfg => cfg.AddHandler<ExampleMessageHandler, ExampleMessage>());
        var context = CreateDbContext();
        var message = new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new ExampleMessage(10));

        // Act
        await context.AddMessagesAsync(serviceProvider, [message], cancellationToken);

        // Assert: the database defaulted the instant to its own present, rather than leaving it unset
        var secondsUntilVisible = await context.SecondsUntilVisibleAsync(message.Id, cancellationToken);
        Assert.InRange(secondsUntilVisible, -60, 0);
    }

    [Fact]
    public async Task FailedMessageIsNotHandledAgainUntilItsVisibilityInstantHasPassed()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var serviceProvider = BuildServiceProvider(cfg =>
        {
            cfg.AddHandler<FailedMessageHandler, FailedMessage>();
            cfg.BackoffBase = TimeSpan.FromMinutes(10);
            cfg.BackoffJitter = 0;
        });
        var processor = serviceProvider.GetRequiredService<ConcurrentProcessor<OutboxMessage>>();
        var context = CreateDbContext();
        var message = new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new FailedMessage(10));
        await context.AddMessagesAsync(serviceProvider, [message], cancellationToken);

        // Act
        await processor.ProcessUntilIdleAsync(cancellationToken);
        var afterFirstAttempt = FailedMessageHandler.CalledWith.Count;

        // a further run while the message is still in backoff must not reach the handler
        await processor.ProcessUntilIdleAsync(cancellationToken);
        var whileInBackoff = FailedMessageHandler.CalledWith.Count;

        // simulate the backoff elapsing instead of waiting ten minutes for it
        await context.MakeUnhandledMessagesVisibleAsync(cancellationToken);
        await processor.ProcessUntilIdleAsync(cancellationToken);

        // Assert
        Assert.Equal(1, afterFirstAttempt);
        Assert.Equal(1, whileInBackoff);
        Assert.Equal(2, FailedMessageHandler.CalledWith.Count);
    }

    [Fact]
    public async Task SuccessiveFailuresAreDelayedFurtherEachTimeUntilTheDelayReachesTheCap()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var serviceProvider = BuildServiceProvider(cfg =>
        {
            cfg.AddHandler<FailedMessageHandler, FailedMessage>();
            cfg.BackoffBase = TimeSpan.FromSeconds(10);
            cfg.MaxBackoff = TimeSpan.FromSeconds(40);
            cfg.BackoffJitter = 0;
        });
        var processor = serviceProvider.GetRequiredService<ConcurrentProcessor<OutboxMessage>>();
        var context = CreateDbContext();
        var message = new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new FailedMessage(10));
        await context.AddMessagesAsync(serviceProvider, [message], cancellationToken);

        double[] expectedDelays = [10, 20, 40, 40];
        var actualDelays = new List<double>();

        // Act
        foreach (var _ in expectedDelays)
        {
            await processor.ProcessUntilIdleAsync(cancellationToken);
            actualDelays.Add(await context.SecondsUntilVisibleAsync(message.Id, cancellationToken));
            await context.MakeUnhandledMessagesVisibleAsync(cancellationToken);
        }

        // Assert
        Assert.Equal(expectedDelays.Length, FailedMessageHandler.CalledWith.Count);
        Assert.All(
            actualDelays.Zip(expectedDelays),
            // the few milliseconds each attempt takes are why this is a range and not an equality: the delay
            // is measured against the database clock some way into the interval it granted
            delays => Assert.InRange(delays.First, delays.Second - 2, delays.Second));
    }

    /// <summary>
    /// The one test that really waits. Everything else moves the stored instant instead, which proves the
    /// rule but not that a delay left to itself ever expires - so this one keeps the whole wiring honest,
    /// at the cost of a fraction of a second.
    /// </summary>
    [Fact]
    public async Task BackoffElapsesOnItsOwnWithoutTheStoredInstantBeingMoved()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var serviceProvider = BuildServiceProvider(cfg =>
        {
            cfg.AddHandler<FailedMessageHandler, FailedMessage>();
            cfg.BackoffBase = TimeSpan.FromMilliseconds(250);
            cfg.BackoffJitter = 0;
        });
        var processor = serviceProvider.GetRequiredService<ConcurrentProcessor<OutboxMessage>>();
        var context = CreateDbContext();
        var message = new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new FailedMessage(10));
        await context.AddMessagesAsync(serviceProvider, [message], cancellationToken);

        // Act
        await processor.ProcessUntilIdleAsync(cancellationToken);
        var afterFirstAttempt = FailedMessageHandler.CalledWith.Count;

        // comfortably longer than the backoff, so that a slow run cannot turn this into a race
        await Task.Delay(TimeSpan.FromMilliseconds(750), cancellationToken);
        await processor.ProcessUntilIdleAsync(cancellationToken);

        // Assert
        Assert.Equal(1, afterFirstAttempt);
        Assert.Equal(2, FailedMessageHandler.CalledWith.Count);
    }

    private ServiceProvider BuildServiceProvider(Action<OutboxServiceConfiguration> configure)
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddOutboxServices<TestDbContext>(configure);
        serviceCollection.AddBaseServices(Container, _testOutputHelper);

        return serviceCollection.BuildServiceProvider();
    }
}
