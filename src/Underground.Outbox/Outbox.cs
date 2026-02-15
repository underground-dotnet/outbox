using Underground.Outbox.Data;
using Underground.Outbox.Domain;

namespace Underground.Outbox;

internal sealed class Outbox(AddMessagesToOutbox addMessage, ConcurrentProcessor<OutboxMessage> processor) : IOutbox
{
    public async Task AddMessageAsync(IOutboxDbContext context, OutboxMessage message, CancellationToken cancellationToken)
    {
        await addMessage.ExecuteAsync(context, [message], cancellationToken);
    }

    public async Task AddMessagesAsync(IOutboxDbContext context, IEnumerable<OutboxMessage> messages, CancellationToken cancellationToken)
    {
        await addMessage.ExecuteAsync(context, messages, cancellationToken);
    }

    public void ProcessMessages()
    {
        processor.ScheduleProcessingRun();
    }
}
