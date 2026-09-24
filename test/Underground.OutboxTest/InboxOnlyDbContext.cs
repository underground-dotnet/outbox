using Microsoft.EntityFrameworkCore;

using Underground.Outbox.Data;

namespace Underground.OutboxTest;

/// <summary>
/// A context on the inbox side alone, so that a test can put the inbox and the outbox in different
/// contexts, neither of which maps the other's table.
/// </summary>
public class InboxOnlyDbContext(DbContextOptions<InboxOnlyDbContext> options) : DbContext(options), IInboxDbContext
{
    public DbSet<InboxMessage> InboxMessages { get; set; }
}
