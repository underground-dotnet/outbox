using Underground.Outbox.Data;
using Underground.Outbox.Domain;

namespace Underground.Outbox;

internal sealed class OutboxImpl(AddMessagesToOutbox addMessage, ConcurrentProcessor<OutboxMessage> processor) : IOutbox
{
    public async Task AddMessageAsync(IOutboxDbContext context, OutboxMessage message, CancellationToken cancellationToken)
    {
        await addMessage.ExecuteAsync(context, [message], cancellationToken).ConfigureAwait(false);
    }

    public async Task AddMessagesAsync(IOutboxDbContext context, IEnumerable<OutboxMessage> messages, CancellationToken cancellationToken)
    {
        await addMessage.ExecuteAsync(context, messages, cancellationToken).ConfigureAwait(false);
    }

    public void ProcessMessages()
    {
        processor.ScheduleProcessingRun();
    }
}
