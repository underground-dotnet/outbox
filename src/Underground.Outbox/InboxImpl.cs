using Underground.Outbox.Data;
using Underground.Outbox.Domain;

namespace Underground.Outbox;

internal sealed class InboxImpl(AddMessagesToInbox addMessage, ConcurrentProcessor<InboxMessage> processor) : IInbox
{
    public async Task AddMessageAsync(IInboxDbContext context, InboxMessage message, CancellationToken cancellationToken)
    {
        await addMessage.ExecuteAsync(context, [message], cancellationToken).ConfigureAwait(false);
    }

    public async Task AddMessagesAsync(IInboxDbContext context, IEnumerable<InboxMessage> messages, CancellationToken cancellationToken)
    {
        await addMessage.ExecuteAsync(context, messages, cancellationToken).ConfigureAwait(false);
    }

    public void StageMessage(IInboxDbContext context, InboxMessage message)
    {
        addMessage.Stage(context, [message]);
    }

    public void StageMessages(IInboxDbContext context, IEnumerable<InboxMessage> messages)
    {
        addMessage.Stage(context, messages);
    }

    public void ProcessMessages()
    {
        processor.NotifyWork();
    }
}
