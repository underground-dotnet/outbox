using Microsoft.EntityFrameworkCore;

using Underground.Outbox.Data;
using Underground.Outbox.Domain;

namespace Underground.Outbox;

internal sealed class InboxImpl<TContext>(
    AddMessagesToInbox<TContext> addMessage,
    ConcurrentProcessor<TContext, InboxMessage> processor
) : IInbox<TContext> where TContext : DbContext, IInboxDbContext
{
    public async Task AddMessageAsync(TContext context, InboxMessage message, CancellationToken cancellationToken)
    {
        await addMessage.ExecuteAsync(context, [message], cancellationToken).ConfigureAwait(false);
    }

    public async Task AddMessagesAsync(TContext context, IEnumerable<InboxMessage> messages, CancellationToken cancellationToken)
    {
        await addMessage.ExecuteAsync(context, messages, cancellationToken).ConfigureAwait(false);
    }

    public void ProcessMessages()
    {
        processor.NotifyWork();
    }
}
