using Underground.Outbox.Configuration;

namespace Underground.Outbox.Domain.Inboxes;

/// <summary>
/// What one <c>AddInboxServices</c> call declared: an Inbox's name, the context its transaction runs on, and
/// its settings. <see cref="InboxDirectory"/> builds the running <see cref="Inbox"/> from it.
/// </summary>
internal sealed record InboxRegistration(string Name, Type ContextType, InboxServiceConfiguration Configuration);
