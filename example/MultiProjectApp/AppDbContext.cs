using Microsoft.EntityFrameworkCore;

using Underground.Outbox.Data;

namespace MultiProjectApp;

sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IOutboxDbContext, IInboxDbContext
{
    public DbSet<OutboxMessage> OutboxMessages { get; set; }
    public DbSet<InboxMessage> InboxMessages { get; set; }
}
