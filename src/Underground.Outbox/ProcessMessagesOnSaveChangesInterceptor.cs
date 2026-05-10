using System.Data.Common;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Underground.Outbox.Data;

namespace Underground.Outbox;

/// <summary>
/// Triggers push-based inbox/outbox processing after a successful save when new inbox or outbox messages were added.
/// </summary>
public sealed partial class ProcessMessagesOnSaveChangesInterceptor(IServiceProvider serviceProvider, ILogger<ProcessMessagesOnSaveChangesInterceptor> logger) : DbTransactionInterceptor, ISaveChangesInterceptor
{
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private bool _hasOutboxChanges;
    private bool _hasInboxChanges;
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

        var processOutbox = context is IOutboxDbContext && context.ChangeTracker.Entries<OutboxMessage>().Any(entry => entry.State == EntityState.Added);
        var processInbox = context is IInboxDbContext && context.ChangeTracker.Entries<InboxMessage>().Any(entry => entry.State == EntityState.Added);

        if (!processOutbox && !processInbox)
        {
            return;
        }

        Interlocked.Exchange(ref _hasOutboxChanges, processOutbox);
        Interlocked.Exchange(ref _hasInboxChanges, processInbox);
    }

    private void TriggerProcessing()
    {
        if (_hasOutboxChanges)
        {
            LogNewMessagesDetected("outbox");
            _serviceProvider.GetRequiredService<IOutbox>().ProcessMessages();
            Interlocked.Exchange(ref _hasOutboxChanges, false);
        }

        if (_hasInboxChanges)
        {
            LogNewMessagesDetected("inbox");
            _serviceProvider.GetRequiredService<IInbox>().ProcessMessages();
            Interlocked.Exchange(ref _hasInboxChanges, false);
        }
    }

    [LoggerMessage(
            EventId = 1,
            Level = LogLevel.Debug,
            Message = "New {Type} messages detected for processing")]
    private partial void LogNewMessagesDetected(string Type);
}
