using Microsoft.EntityFrameworkCore;

using Underground.Outbox.Data;
using Underground.Outbox.Domain;

namespace Underground.Outbox;

internal sealed class OutboxImpl<TContext>(
    AddMessagesToOutbox<TContext> addMessage,
    ConcurrentProcessor<TContext, OutboxMessage> processor
) : IOutbox<TContext> where TContext : DbContext, IOutboxDbContext
{
    public async Task AddMessageAsync(TContext context, OutboxMessage message, CancellationToken cancellationToken)
    {
        await addMessage.ExecuteAsync(context, [message], cancellationToken).ConfigureAwait(false);
    }

    public async Task AddMessagesAsync(TContext context, IEnumerable<OutboxMessage> messages, CancellationToken cancellationToken)
    {
        await addMessage.ExecuteAsync(context, messages, cancellationToken).ConfigureAwait(false);
    }

    public void ProcessMessages()
    {
        processor.NotifyWork();
    }
}
