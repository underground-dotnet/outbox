using System.Data.Common;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Underground.Outbox.Data;

namespace Underground.Outbox;

/// <summary>
/// Triggers push-based inbox/outbox processing after a successful commit when new inbox or outbox messages were added.
/// </summary>
/// <remarks>
/// What is pending belongs to the transaction that staged it, which is why the transaction is recorded
/// alongside: a save under a different one discards what an abandoned predecessor left, so a transaction that
/// never committed cannot make a later one notify. Plain fields suffice: the interceptor is scoped alongside
/// its <see cref="DbContext"/>, which forbids concurrent use.
/// </remarks>
public sealed partial class ProcessMessagesOnSaveChangesInterceptor(IServiceProvider serviceProvider, ILogger<ProcessMessagesOnSaveChangesInterceptor> logger) : DbTransactionInterceptor, ISaveChangesInterceptor
{
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private bool _hasOutboxChanges;
    private readonly HashSet<string> _inboxesWithChanges = new(StringComparer.Ordinal);
    private Guid? _stagingTransactionId;
    private readonly ILogger<ProcessMessagesOnSaveChangesInterceptor> _logger = logger;

    public override void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData)
    {
        TriggerProcessing();
    }

    public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        TriggerProcessing();
        return Task.CompletedTask;
    }

    public override void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData)
    {
        ClearPendingChanges();
    }

    public override Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        ClearPendingChanges();
        return Task.CompletedTask;
    }

    public InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        CheckForNewInboxOutboxEntities(eventData.Context);
        return result;
    }

    public ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default
    )
    {
        CheckForNewInboxOutboxEntities(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void CheckForNewInboxOutboxEntities(DbContext? context)
    {
        if (context is null)
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
        if (context is IInboxDbContext)
        {
            _inboxesWithChanges.UnionWith(context.ChangeTracker.Entries<InboxMessage>()
                .Where(entry => entry.State == EntityState.Added)
                .Select(entry => entry.Entity.Inbox));
        }
    }

    private void TriggerProcessing()
    {
        var processOutbox = _hasOutboxChanges;
        string[] inboxes = [.. _inboxesWithChanges];
        ClearPendingChanges();

        if (processOutbox)
        {
            LogNewMessagesDetected("outbox");
            _serviceProvider.GetRequiredService<IOutbox>().ProcessMessages();
        }

        if (inboxes.Length > 0)
        {
            LogNewMessagesDetected("inbox");

            // only the Inboxes the transaction wrote to: the others have nothing new to look at
            var inboxService = _serviceProvider.GetRequiredService<IInbox>();
            foreach (var inbox in inboxes)
            {
                inboxService.ProcessMessages(inbox);
            }
        }
    }

    private void ClearPendingChanges()
    {
        _hasOutboxChanges = false;
        _inboxesWithChanges.Clear();
        _stagingTransactionId = null;
    }

    [LoggerMessage(
            EventId = 1,
            Level = LogLevel.Debug,
            Message = "New {Type} messages detected for processing")]
    private partial void LogNewMessagesDetected(string Type);
}
