using Microsoft.EntityFrameworkCore;

using Underground.Outbox.Data;

namespace Underground.Outbox;

public interface IOutbox
{
    /// <summary>
    /// Adds a message to the outbox.
    /// </summary>
    /// <param name="context"></param>
    /// <param name="message"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    /// <exception cref="DbUpdateException">
    /// When a message with the same EventId already exists in the outbox.
    /// </exception>
    public Task AddMessageAsync(IOutboxDbContext context, OutboxMessage message, CancellationToken cancellationToken);

    /// <summary>
    /// Adds a message to the outbox.
    /// </summary>
    /// <param name="context"></param>
    /// <param name="messages"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    /// <exception cref="DbUpdateException">
    /// When a message with the same EventId already exists in the outbox.
    /// </exception>
    public Task AddMessagesAsync(IOutboxDbContext context, IEnumerable<OutboxMessage> messages, CancellationToken cancellationToken);

    public void ProcessMessages();
}
