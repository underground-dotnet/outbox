using Microsoft.EntityFrameworkCore;

using Underground.Outbox.Data;

namespace Underground.Outbox;

public interface IInbox
{
    /// <summary>
    /// Adds a message to the inbox: stages it and saves it, within a transaction the caller has already opened.
    /// </summary>
    /// <exception cref="DbUpdateException">
    /// When a message with the same EventId already exists in the inbox.
    /// </exception>
    public Task AddMessageAsync(IInboxDbContext context, InboxMessage message, CancellationToken cancellationToken);

    /// <summary>
    /// Adds several messages to the inbox: stages them and saves them, within a transaction the caller has
    /// already opened.
    /// </summary>
    /// <exception cref="DbUpdateException">
    /// When a message with the same EventId already exists in the inbox.
    /// </exception>
    public Task AddMessagesAsync(IInboxDbContext context, IEnumerable<InboxMessage> messages, CancellationToken cancellationToken);

    /// <summary>
    /// Stages a message in the caller's unit of work, without saving it.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="AddMessageAsync"/>, this neither saves nor requires an active transaction — the
    /// caller owns both. The message is written by whichever <c>SaveChanges</c> the caller makes next, so it
    /// is the caller who must ensure that save carries the business change the message belongs to. A message
    /// staged outside an explicit transaction triggers no push-based processing; it is picked up by the next
    /// processing cycle, or at once if the caller calls <see cref="ProcessMessages"/>.
    /// </remarks>
    public void StageMessage(IInboxDbContext context, InboxMessage message);

    /// <summary>
    /// Stages several messages in the caller's unit of work, without saving them.
    /// </summary>
    /// <remarks>
    /// See <see cref="StageMessage"/>: the caller owns the save and the transaction.
    /// </remarks>
    public void StageMessages(IInboxDbContext context, IEnumerable<InboxMessage> messages);

    /// <summary>
    /// Triggers a processing run in the background.
    /// </summary>
    public void ProcessMessages();
}
