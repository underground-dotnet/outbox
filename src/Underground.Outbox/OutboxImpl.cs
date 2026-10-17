using Underground.Outbox.Data;
using Underground.Outbox.Domain;

namespace Underground.Outbox;

internal sealed class OutboxImpl(ConcurrentProcessor<OutboxMessage> processor)
    : MessageBox<OutboxMessage, IOutboxDbContext>(processor), IOutbox;
