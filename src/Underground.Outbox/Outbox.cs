using Underground.Outbox.Data;
using Underground.Outbox.Domain;

namespace Underground.Outbox;

internal sealed class Outbox(AddMessageToOutbox addMessage) : IOutbox
{
    public async Task AddMessageAsync(IOutboxDbContext context, OutboxMessage message, CancellationToken cancellationToken)
    {
        await addMessage.ExecuteAsync(context, message, cancellationToken);
    }

    public async Task AddMessagesAsync(IOutboxDbContext context, IEnumerable<OutboxMessage> messages, CancellationToken cancellationToken)
    {
        foreach (var message in messages)
        {
            await AddMessageAsync(context, message, cancellationToken);
        }
    }

}
