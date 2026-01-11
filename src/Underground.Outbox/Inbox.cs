using Underground.Outbox.Data;
using Underground.Outbox.Domain;

namespace Underground.Outbox;

internal sealed class Inbox(AddMessageToInbox addMessage, ConcurrentProcessor<InboxMessage> processor) : IInbox
{
    public async Task AddMessageAsync(IInboxDbContext context, InboxMessage message, CancellationToken cancellationToken)
    {
        await addMessage.ExecuteAsync(context, message, cancellationToken);
    }

    public async Task AddMessagesAsync(IInboxDbContext context, IEnumerable<InboxMessage> messages, CancellationToken cancellationToken)
    {
        foreach (var message in messages)
        {
            await AddMessageAsync(context, message, cancellationToken);
        }
    }

    public void ProcessMessages()
    {
        processor.ScheduleProcessingRun();
    }
}
