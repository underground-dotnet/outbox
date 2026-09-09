using Microsoft.EntityFrameworkCore;

using Underground.Outbox.Data;

namespace MultiProjectLib;

/// <summary>
/// Lives beside the handlers that name it, because a handler in a referenced assembly has to be able to
/// write <c>[OutboxHandler&lt;AppDbContext&gt;]</c>.
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IOutboxDbContext, IInboxDbContext
{
    public DbSet<OutboxMessage> OutboxMessages { get; set; }
    public DbSet<InboxMessage> InboxMessages { get; set; }
}
