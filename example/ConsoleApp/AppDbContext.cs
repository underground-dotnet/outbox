using Microsoft.EntityFrameworkCore;

using Underground.Outbox.Data;

namespace ConsoleApp;

sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IOutboxDbContext
{
    public DbSet<OutboxMessage> OutboxMessages { get; set; }
}
