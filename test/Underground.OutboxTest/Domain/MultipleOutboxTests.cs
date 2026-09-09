using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Underground.Outbox;
using Underground.Outbox.Data;
using Underground.Outbox.Domain;
using Underground.OutboxTest.TestHandler;

namespace Underground.OutboxTest.Domain;

/// <summary>
/// Two modules in one process, sharing a connection string and a message type, each with its own
/// <see cref="DbContext"/> and its own schema. Every silent failure ADR 0008 records is a case here: a
/// second registration overwriting the first, a second worker never starting, a Claim reaching the wrong
/// schema, and a neighbour's Handler being given this module's message.
/// </summary>
public class MultipleOutboxTests : DatabaseTest
{
    private readonly ServiceProvider _serviceProvider;
    private readonly ILoggerFactory _loggerFactory;

    /// <summary>
    /// How long a notification is waited for. Well under the 4s poll cadence, so a tick of the poll cannot
    /// be mistaken for the commit's own signal.
    /// </summary>
    private static readonly TimeSpan SignalWindow = TimeSpan.FromSeconds(1);

    public MultipleOutboxTests(ITestOutputHelper testOutputHelper) : base(testOutputHelper)
    {
        ModuleAHandler.CalledWith.Clear();
        ModuleBHandler.CalledWith.Clear();

        _loggerFactory = LoggerFactory.Create(builder => builder.ConfigureTestLogger(testOutputHelper));

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.ConfigureTestLogger(testOutputHelper));
        services.AddModuleA(Database, _loggerFactory);
        services.AddModuleB(Database, _loggerFactory);

        _serviceProvider = services.BuildServiceProvider();
    }

    [Fact]
    public async Task EachOutboxClaimsOnlyItsOwnRows()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await WriteToModuleAAsync(1, cancellationToken);
        await WriteToModuleBAsync(2, cancellationToken);

        // Act: module A alone, so anything it claims from module B's schema shows up as a handled message
        await _serviceProvider
            .GetRequiredService<ConcurrentProcessor<ModuleADbContext, OutboxMessage>>()
            .ProcessUntilIdleAsync(cancellationToken);

        // Assert
        Assert.Equal([1], ModuleAHandler.CalledWith);
        Assert.Empty(ModuleBHandler.CalledWith);
        Assert.Equal(1, await IncompleteCountAsync<ModuleBDbContext>(cancellationToken));
    }

    [Fact]
    public async Task EachModulesMessageReachesItsOwnHandlerForAMessageTypeBothHandle()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await WriteToModuleAAsync(1, cancellationToken);
        await WriteToModuleBAsync(2, cancellationToken);

        // Act
        await ProcessBothAsync(cancellationToken);

        // Assert: neither module's Handler saw the other's message, though both handle SharedContract
        Assert.Equal([1], ModuleAHandler.CalledWith);
        Assert.Equal([2], ModuleBHandler.CalledWith);
    }

    [Fact]
    public async Task AClaimReachesTheSchemaTheRegistrationNamed()
    {
        // Arrange: a row written through module A's context, which maps to module A's schema
        var cancellationToken = TestContext.Current.CancellationToken;
        await WriteToModuleAAsync(7, cancellationToken);

        // Act
        await ProcessBothAsync(cancellationToken);

        // Assert: the Claim found it and the Completion wrote back to the same table
        Assert.Equal([7], ModuleAHandler.CalledWith);
        Assert.Equal(0, await IncompleteCountAsync<ModuleADbContext>(cancellationToken));
    }

    [Fact]
    public async Task ACommitInOneModuleWakesOnlyThatModulesWorkers()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var moduleA = _serviceProvider.GetRequiredService<ConcurrentProcessor<ModuleADbContext, OutboxMessage>>();
        var moduleB = _serviceProvider.GetRequiredService<ConcurrentProcessor<ModuleBDbContext, OutboxMessage>>();

        // drain whatever is already buffered, so what is left is only what the commit sent
        await WasSignalledAsync(moduleA, cancellationToken);
        await WasSignalledAsync(moduleB, cancellationToken);

        // Act
        await WriteToModuleAAsync(1, cancellationToken);

        // Assert
        Assert.True(await WasSignalledAsync(moduleA, cancellationToken), "module A's own commit did not wake its workers");
        Assert.False(await WasSignalledAsync(moduleB, cancellationToken), "a commit in module A woke module B's workers");
    }

    [Fact]
    public async Task OneHostedServiceRunsTheWorkersOfEveryRegisteredOutbox()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await WriteToModuleAAsync(1, cancellationToken);
        await WriteToModuleBAsync(2, cancellationToken);

        var hostedServices = _serviceProvider.GetRequiredService<IEnumerable<IHostedService>>().ToList();

        // Act
        foreach (var service in hostedServices)
        {
            await service.StartAsync(cancellationToken);
        }

        try
        {
            SpinWait.SpinUntil(
                () => !ModuleAHandler.CalledWith.IsEmpty && !ModuleBHandler.CalledWith.IsEmpty,
                TimeSpan.FromSeconds(20));
        }
        finally
        {
            foreach (var service in hostedServices)
            {
                await service.StopAsync(cancellationToken);
            }
        }

        // Assert: both modules' workers ran, from the one hosted service that drives message processing
        Assert.Equal([1], ModuleAHandler.CalledWith);
        Assert.Equal([2], ModuleBHandler.CalledWith);
    }

    /// <summary>
    /// Whether the processor is holding a work notification. Consuming it is the only way to observe one,
    /// so this both asks and answers - which is why each test asks once per processor.
    /// </summary>
    private static async Task<bool> WasSignalledAsync<TContext>(ConcurrentProcessor<TContext, OutboxMessage> processor, CancellationToken cancellationToken)
        where TContext : DbContext
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(SignalWindow);

        await processor.WaitForWorkAsync(timeout.Token);

        // WaitForWorkAsync returns rather than throws on cancellation, so the token is what says which
        // of the two ended the wait
        return !timeout.IsCancellationRequested;
    }

    private async Task ProcessBothAsync(CancellationToken cancellationToken)
    {
        await _serviceProvider.GetRequiredService<ConcurrentProcessor<ModuleADbContext, OutboxMessage>>().ProcessUntilIdleAsync(cancellationToken);
        await _serviceProvider.GetRequiredService<ConcurrentProcessor<ModuleBDbContext, OutboxMessage>>().ProcessUntilIdleAsync(cancellationToken);
    }

    private async Task WriteToModuleAAsync(int id, CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ModuleADbContext>();
        var outbox = scope.ServiceProvider.GetRequiredService<IOutbox<ModuleADbContext>>();

        var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await using (transaction.ConfigureAwait(false))
        {
            await outbox.AddMessageAsync(context, new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new SharedContract(id)), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
    }

    private async Task WriteToModuleBAsync(int id, CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ModuleBDbContext>();
        var outbox = scope.ServiceProvider.GetRequiredService<IOutbox<ModuleBDbContext>>();

        var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await using (transaction.ConfigureAwait(false))
        {
            await outbox.AddMessageAsync(context, new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new SharedContract(id)), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
    }

    private async Task<int> IncompleteCountAsync<TContext>(CancellationToken cancellationToken) where TContext : DbContext, IOutboxDbContext
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TContext>();

        return await context.OutboxMessages.CountAsync(m => m.CompletedAt == null, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask ReleaseOwnedResourcesAsync()
    {
        await _serviceProvider.DisposeAsync();
        _loggerFactory.Dispose();
    }
}
