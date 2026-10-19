using Underground.Outbox.Data;
using Underground.Outbox.Domain.Inboxes;
using Underground.Outbox.Exceptions;

namespace Underground.Outbox;

internal sealed class InboxImpl(InboxDirectory inboxes) : MessageBox<InboxMessage, IInboxDbContext>, IInbox
{
    public override void ProcessMessages()
    {
        foreach (var inbox in inboxes.All)
        {
            inbox.Processor.NotifyWork();
        }
    }

    public void ProcessMessages(string inbox) => inboxes.NotifyWork(inbox);

    /// <summary>One copy per Inbox that handles the message's type, so each applies it in its own transaction.</summary>
    protected override IEnumerable<InboxMessage> Address(InboxMessage message)
    {
        var handling = inboxes.Handling(message.Type);
        if (handling.Count == 0)
        {
            throw new NoInboxHandlesMessageException(message.Type);
        }

        return handling.Select(message.AddressedTo);
    }
}
