using Microsoft.EntityFrameworkCore;

using Underground.Outbox.Data;

namespace Underground.Outbox;

public interface IInbox
{
    /// <summary>
    /// Adds a message to the inbox.
    /// </summary>
    /// <exception cref="DbUpdateException">
    /// When a message with the same EventId already exists in the inbox.
    /// </exception>
    public Task AddMessageAsync(IInboxDbContext context, InboxMessage message, CancellationToken cancellationToken);

    /// <summary>
    /// Adds several messages to the inbox.
    /// </summary>
    /// <exception cref="DbUpdateException">
    /// When a message with the same EventId already exists in the inbox.
    /// </exception>
    public Task AddMessagesAsync(IInboxDbContext context, IEnumerable<InboxMessage> messages, CancellationToken cancellationToken);

    /// <summary>
    /// Triggers a processing run in the background.
    /// </summary>
    public void ProcessMessages();
}
