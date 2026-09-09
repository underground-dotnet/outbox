using Microsoft.EntityFrameworkCore;

using Underground.Outbox.Data;

namespace ConsoleApp;

// public because the generated registration entry point names it in a public signature
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IOutboxDbContext, IInboxDbContext
{
    public DbSet<OutboxMessage> OutboxMessages { get; set; }
    public DbSet<InboxMessage> InboxMessages { get; set; }
}
