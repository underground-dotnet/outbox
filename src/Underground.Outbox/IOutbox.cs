using Microsoft.EntityFrameworkCore;

using Underground.Outbox.Data;

namespace Underground.Outbox;

/// <summary>
/// The outbox belonging to one <typeparamref name="TContext"/>. An application holds as many as it has
/// contexts, and the type parameter is what says which module's outbox a caller is writing to.
/// </summary>
/// <typeparam name="TContext">The context whose outbox this is.</typeparam>
public interface IOutbox<in TContext> where TContext : DbContext, IOutboxDbContext
{
    /// <summary>
    /// Adds a message to this outbox.
    /// </summary>
    /// <exception cref="DbUpdateException">
    /// When a message with the same EventId already exists in the outbox.
    /// </exception>
    public Task AddMessageAsync(TContext context, OutboxMessage message, CancellationToken cancellationToken);

    /// <summary>
    /// Adds several messages to this outbox.
    /// </summary>
    /// <exception cref="DbUpdateException">
    /// When a message with the same EventId already exists in the outbox.
    /// </exception>
    public Task AddMessagesAsync(TContext context, IEnumerable<OutboxMessage> messages, CancellationToken cancellationToken);

    /// <summary>
    /// Triggers a processing run in the background, for this outbox alone.
    /// </summary>
    public void ProcessMessages();
}
