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
    where TEntity : class, IMessage, ITraceContextCarrier
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

    public void StageMessages(TContext context, IEnumerable<TEntity> messages)
    {
        // materialised before anything is added, so a message that cannot be addressed stages none of them
        List<TEntity> toAdd = [.. messages.SelectMany(Address)];

        var traceParent = Activity.Current?.Id;
        foreach (var message in toAdd)
        {
            message.SetTraceParent(traceParent);
        }

        context.Set<TEntity>().AddRange(toAdd);
    }

    public void ProcessMessages() => processor.NotifyWork();

    /// <summary>The rows one message is stored as. One, unless the side addresses a row to each of its Handlers.</summary>
    protected virtual IEnumerable<TEntity> Address(TEntity message) => [message];
}
