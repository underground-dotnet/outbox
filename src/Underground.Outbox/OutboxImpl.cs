using Underground.Outbox.Data;
using Underground.Outbox.Domain;

namespace Underground.Outbox;

internal sealed class OutboxImpl(ConcurrentProcessor<OutboxMessage> processor)
    : MessageBox<OutboxMessage, IOutboxDbContext>, IOutbox
{
    public override void ProcessMessages() => processor.NotifyWork();

    protected override IEnumerable<OutboxMessage> Address(OutboxMessage message) => [message];
}
