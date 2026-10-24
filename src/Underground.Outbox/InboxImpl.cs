using Underground.Outbox.Data;
using Underground.Outbox.Domain;
using Underground.Outbox.Domain.Dispatchers;
using Underground.Outbox.Exceptions;

namespace Underground.Outbox;

internal sealed class InboxImpl(ConcurrentProcessor<InboxMessage> processor, HandlerRegistry<InboxMessage> registry)
    : MessageBox<InboxMessage, IInboxDbContext>(processor), IInbox
{
    // one row per inbox Handler of the type, so each applies it in a transaction of its own (ADR 0012)
    protected override IEnumerable<InboxMessage> Address(InboxMessage message)
    {
        var entries = registry.EntriesFor(message.Type);
        if (entries.Count == 0)
        {
            throw new NoInboxHandlerException(message.Type);
        }

        // the caller's instance is the first Recipient's row, so its Id is filled in by the save as before
        message.Recipient = entries[0].Recipient;

        return [message, .. entries.Skip(1).Select(entry => message.AddressedTo(entry.Recipient))];
    }
}
