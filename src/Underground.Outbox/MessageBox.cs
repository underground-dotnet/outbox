using System.Diagnostics;

using Underground.Outbox.Data;
using Underground.Outbox.Domain;
using Underground.Outbox.Exceptions;

namespace Underground.Outbox;

/// <summary>
/// Adds and Stages messages for one side, and wakes that side's workers. <see cref="IOutbox"/> and
/// <see cref="IInbox"/> are this closed over each side's types, so a change to either path lands on both.
/// </summary>
internal abstract class MessageBox<TEntity, TContext>(ConcurrentProcessor<TEntity> processor)
    where TEntity : class, IMessage, ICarriesCreationContext
    where TContext : IDbContext
{
    public Task AddMessageAsync(TContext context, TEntity message, CancellationToken cancellationToken)
        => AddMessagesAsync(context, [message], cancellationToken);

    public async Task AddMessagesAsync(TContext context, IEnumerable<TEntity> messages, CancellationToken cancellationToken)
    {
        // Adding promises the message commits with the business change, which only a transaction the caller
        // already opened can guarantee; without one, SaveChanges would commit the message on its own.
        if (context.Database.CurrentTransaction is null)
        {
            throw new NoActiveTransactionException(TEntity.TableName);
        }

        StageMessages(context, messages);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public void StageMessage(TContext context, TEntity message) => StageMessages(context, [message]);

    // an instance member because it implements IOutbox and IInbox
#pragma warning disable CA1822, S2325 // Mark members as static
    public void StageMessages(TContext context, IEnumerable<TEntity> messages)
#pragma warning restore CA1822, S2325 // Mark members as static
    {
        // materialised once: the trace context is stamped in a pass of its own, and AddRange enumerates again
        var toAdd = messages as IReadOnlyCollection<TEntity> ?? [.. messages];

        var traceParent = Activity.Current?.Id;
        foreach (var message in toAdd)
        {
            message.StampCreationContext(traceParent);
        }

        context.Set<TEntity>().AddRange(toAdd);
    }

    public void ProcessMessages() => processor.NotifyWork();
}
