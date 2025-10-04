using Microsoft.EntityFrameworkCore;

using Underground.Outbox.Data;

namespace ConsoleApp;

sealed class AppDbContext : DbContext, IOutboxDbContext
{
    public DbSet<OutboxMessage> OutboxMessages { get; set; }
}
