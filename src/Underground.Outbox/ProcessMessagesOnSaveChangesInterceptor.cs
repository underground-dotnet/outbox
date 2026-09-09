using System.Data.Common;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Underground.Outbox.Data;
using Underground.Outbox.Domain;

namespace Underground.Outbox;

/// <summary>
/// Triggers push-based processing after a successful commit on <typeparamref name="TContext"/>, when that
/// commit added inbox or outbox messages.
/// </summary>
/// <remarks>
/// Bound to one context, so a commit in one module wakes that module's workers and no one else's. What is
/// pending belongs to the transaction that staged it, which is why the transaction is recorded alongside: a
/// save under a different one discards what an abandoned predecessor left, so a transaction that never
/// committed cannot make a later one notify. Plain fields suffice: the interceptor is scoped alongside its
/// <see cref="DbContext"/>, which forbids concurrent use.
/// </remarks>
/// <typeparam name="TContext">The context this interceptor is registered on.</typeparam>
public sealed partial class ProcessMessagesOnSaveChangesInterceptor<TContext>(
    IServiceProvider serviceProvider,
    ILogger<ProcessMessagesOnSaveChangesInterceptor<TContext>> logger
) : DbTransactionInterceptor, ISaveChangesInterceptor where TContext : DbContext
{
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private bool _hasOutboxChanges;
    private bool _hasInboxChanges;
    private Guid? _stagingTransactionId;
    private readonly ILogger<ProcessMessagesOnSaveChangesInterceptor<TContext>> _logger = logger;

    /// <inheritdoc />
    public override void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData)
    {
        TriggerProcessing();
    }

    /// <inheritdoc />
    public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        TriggerProcessing();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData)
    {
        ClearPendingChanges();
    }

    /// <inheritdoc />
    public override Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        ClearPendingChanges();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        CheckForNewInboxOutboxEntities(eventData.Context);
        return result;
    }

    /// <inheritdoc />
    public ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(eventData);

        CheckForNewInboxOutboxEntities(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void CheckForNewInboxOutboxEntities(DbContext? context)
    {
        // a context of another type is another module's, whose own interceptor answers for it
        if (context is not TContext)
        {
            return;
        }

        var transactionId = context.Database.CurrentTransaction?.TransactionId;
        if (transactionId != _stagingTransactionId)
        {
            ClearPendingChanges();
            _stagingTransactionId = transactionId;
        }

        // accumulate: a transaction stages over several saves, and a later one adding nothing says nothing
        // about what an earlier one added
        _hasOutboxChanges |= context is IOutboxDbContext && context.ChangeTracker.Entries<OutboxMessage>().Any(entry => entry.State == EntityState.Added);
        _hasInboxChanges |= context is IInboxDbContext && context.ChangeTracker.Entries<InboxMessage>().Any(entry => entry.State == EntityState.Added);
    }

    private void TriggerProcessing()
    {
        var processOutbox = _hasOutboxChanges;
        var processInbox = _hasInboxChanges;
        ClearPendingChanges();

        // GetService rather than GetRequiredService: a module may register only one of the two sides
        if (processOutbox && _serviceProvider.GetService<IWorkSignal<TContext, OutboxMessage>>() is { } outbox)
        {
            LogNewMessagesDetected("outbox");
            outbox.ProcessMessages();
        }

        if (processInbox && _serviceProvider.GetService<IWorkSignal<TContext, InboxMessage>>() is { } inbox)
        {
            LogNewMessagesDetected("inbox");
            inbox.ProcessMessages();
        }
    }

    private void ClearPendingChanges()
    {
        _hasOutboxChanges = false;
        _hasInboxChanges = false;
        _stagingTransactionId = null;
    }

    [LoggerMessage(
            EventId = 1,
            Level = LogLevel.Debug,
            Message = "New {Type} messages detected for processing")]
    private partial void LogNewMessagesDetected(string Type);
}
