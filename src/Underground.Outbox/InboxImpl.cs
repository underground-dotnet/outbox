using Underground.Outbox.Data;
using Underground.Outbox.Domain;

namespace Underground.Outbox;

internal sealed class InboxImpl(ConcurrentProcessor<InboxMessage> processor)
    : MessageBox<InboxMessage, IInboxDbContext>(processor), IInbox;
