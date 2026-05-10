using System.Data.Common;
using System.Runtime.CompilerServices;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Underground.Outbox.Data;

namespace Underground.Outbox;

/// <summary>
/// Triggers push-based inbox/outbox processing after a successful save when new inbox or outbox messages were added.
/// </summary>
public sealed class ProcessMessagesOnSaveChangesInterceptor(IServiceProvider serviceProvider, ILogger<ProcessMessagesOnSaveChangesInterceptor> logger) : DbTransactionInterceptor, ISaveChangesInterceptor
{
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private bool _hasOutboxChanges = false;
    private bool _hasInboxChanges = false;
    private readonly ILogger<ProcessMessagesOnSaveChangesInterceptor> _logger = logger;

    public override void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData)
    {

    }

    public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        // var context = eventData.Context;
        // if (context is null)
        // {
        //     return Task.CompletedTask;
        // }

        // var processOutbox = context is IOutboxDbContext && context.ChangeTracker.Entries<OutboxMessage>().Any(entry => entry.State == EntityState.Added);
        // var processInbox = context is IInboxDbContext && context.ChangeTracker.Entries<InboxMessage>().Any(entry => entry.State == EntityState.Added);

        // if (processOutbox)
        // {
        //     _logger.LogInformation("!!!!!!!!!!!New outbox messages were added, triggering processing from interceptor.");
        //     _serviceProvider.GetRequiredService<IOutbox>().ProcessMessages();
        // }

        // if (processInbox)
        // {
        //     _serviceProvider.GetRequiredService<IInbox>().ProcessMessages();
        // }

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

    // public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    // {
    //     TriggerProcessing(eventData.Context);
    //     return result;
    // }

    // public override ValueTask<int> SavedChangesAsync(
    //     SaveChangesCompletedEventData eventData,
    //     int result,
    //     CancellationToken cancellationToken = default
    // )
    // {
    //     TriggerProcessing(eventData.Context);
    //     return ValueTask.FromResult(result);
    // }

    public void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        // ClearPendingProcessing(eventData.Context);
    }

    public Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        // ClearPendingProcessing(eventData.Context);
        return Task.CompletedTask;
    }

    private void CheckForNewInboxOutboxEntities(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var processOutbox = context is IOutboxDbContext && context.ChangeTracker.Entries<OutboxMessage>().Any(entry => entry.State == EntityState.Added);
        var processInbox = context is IInboxDbContext && context.ChangeTracker.Entries<InboxMessage>().Any(entry => entry.State == EntityState.Added);

        // ClearPendingProcessing(context);

        if (!processOutbox && !processInbox)
        {
            return;
        }

        // _pendingProcessing.Add(context, new PendingProcessingState(processOutbox, processInbox));
        Interlocked.Exchange(ref _hasOutboxChanges, processOutbox);
        Interlocked.Exchange(ref _hasInboxChanges, processInbox);
    }

    private void TriggerProcessing()
    {
        if (_hasOutboxChanges)
        {
            Interlocked.Exchange(ref _hasOutboxChanges, false);
            _logger.LogInformation("!!!!!!!!!!!New outbox messages were added, triggering processing from interceptor.");
            _serviceProvider.GetRequiredService<IOutbox>().ProcessMessages();
        }

        if (_hasInboxChanges)
        {
            Interlocked.Exchange(ref _hasInboxChanges, false);
            _serviceProvider.GetRequiredService<IInbox>().ProcessMessages();
        }
    }
}
