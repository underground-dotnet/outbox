namespace Underground.Outbox.Domain.Inboxes;

/// <summary>
/// The Inbox a scope works for. The inbox's scoped services read its settings, Handlers and context from
/// here, so one set of registrations serves every Inbox.
/// </summary>
internal sealed class InboxScope
{
    private Inbox? _inbox;

    internal Inbox Inbox
    {
        get => _inbox ?? throw new InvalidOperationException(
            "Inbox services were resolved from a scope that works for no Inbox. Only the Inbox's own workers may resolve them.");
        set => _inbox = value;
    }
}
