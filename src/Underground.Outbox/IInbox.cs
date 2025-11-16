using Microsoft.EntityFrameworkCore;

using Underground.Outbox.Data;

namespace Underground.Outbox;

public interface IInbox
{
    /// <summary>
    /// Adds a message to the inbox.
    /// </summary>
    /// <param name="context"></param>
    /// <param name="message"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    /// <exception cref="DbUpdateException">
    /// When a message with the same EventId already exists in the inbox.
    /// </exception>
    public Task AddMessageAsync(IInboxDbContext context, InboxMessage message, CancellationToken cancellationToken);

    /// <summary>
    /// Adds a message to the inbox.
    /// </summary>
    /// <param name="context"></param>
    /// <param name="messages"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    /// <exception cref="DbUpdateException">
    /// When a message with the same EventId already exists in the inbox.
    /// </exception>
    public Task AddMessagesAsync(IInboxDbContext context, IEnumerable<InboxMessage> messages, CancellationToken cancellationToken);

    /// <summary>
    /// Trigger a processing run of the outbox messages. It will run asynchronously in the background.
    /// </summary>
    public void ProcessMessages();
}
