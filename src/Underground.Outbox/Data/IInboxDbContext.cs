using Microsoft.EntityFrameworkCore;

namespace Underground.Outbox.Data;

/// <summary>
/// Represents a database context that contains the inbox messages.
/// </summary>
public interface IInboxDbContext
{
    /// <summary>
    /// Gets or sets the set of inbox messages.
    /// </summary>
    DbSet<InboxMessage> InboxMessages { get; set; }
}
