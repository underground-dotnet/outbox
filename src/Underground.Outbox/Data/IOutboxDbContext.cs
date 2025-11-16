using Microsoft.EntityFrameworkCore;

namespace Underground.Outbox.Data;

/// <summary>
/// Represents a database context that contains the outbox messages.
/// </summary>
public interface IOutboxDbContext : IDbContext
{
    /// <summary>
    /// Gets or sets the set of outbox messages.
    /// </summary>
    DbSet<OutboxMessage> OutboxMessages { get; set; }
}
