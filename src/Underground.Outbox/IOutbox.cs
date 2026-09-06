using Microsoft.EntityFrameworkCore;

using Underground.Outbox.Data;

namespace Underground.Outbox;

public interface IOutbox
{
    /// <summary>
    /// Adds a message to the outbox.
    /// </summary>
    /// <exception cref="DbUpdateException">
    /// When a message with the same EventId already exists in the outbox.
    /// </exception>
    public Task AddMessageAsync(IOutboxDbContext context, OutboxMessage message, CancellationToken cancellationToken);

    /// <summary>
    /// Adds several messages to the outbox.
    /// </summary>
    /// <exception cref="DbUpdateException">
    /// When a message with the same EventId already exists in the outbox.
    /// </exception>
    public Task AddMessagesAsync(IOutboxDbContext context, IEnumerable<OutboxMessage> messages, CancellationToken cancellationToken);

    /// <summary>
    /// Triggers a processing run in the background.
    /// </summary>
    public void ProcessMessages();
}
