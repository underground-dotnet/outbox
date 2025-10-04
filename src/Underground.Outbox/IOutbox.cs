using Underground.Outbox.Data;

namespace Underground.Outbox;

public interface IOutbox
{
    public Task AddMessageAsync(IOutboxDbContext context, OutboxMessage message);

    public Task AddMessagesAsync(IOutboxDbContext context, IEnumerable<OutboxMessage> messages);
}
