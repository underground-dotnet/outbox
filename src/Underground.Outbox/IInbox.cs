using Microsoft.EntityFrameworkCore;

using Underground.Outbox.Data;

namespace Underground.Outbox;

/// <summary>
/// The inbox belonging to one <typeparamref name="TContext"/>. An application holds as many as it has
/// contexts, and the type parameter is what says which module's inbox a caller is writing to.
/// </summary>
/// <typeparam name="TContext">The context whose inbox this is.</typeparam>
public interface IInbox<in TContext> where TContext : DbContext, IInboxDbContext
{
    /// <summary>
    /// Adds a message to this inbox.
    /// </summary>
    /// <exception cref="DbUpdateException">
    /// When a message with the same EventId already exists in the inbox.
    /// </exception>
    public Task AddMessageAsync(TContext context, InboxMessage message, CancellationToken cancellationToken);

    /// <summary>
    /// Adds several messages to this inbox.
    /// </summary>
    /// <exception cref="DbUpdateException">
    /// When a message with the same EventId already exists in the inbox.
    /// </exception>
    public Task AddMessagesAsync(TContext context, IEnumerable<InboxMessage> messages, CancellationToken cancellationToken);

    /// <summary>
    /// Triggers a processing run in the background, for this inbox alone.
    /// </summary>
    public void ProcessMessages();
}
