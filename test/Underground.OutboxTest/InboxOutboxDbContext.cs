using Microsoft.EntityFrameworkCore;

using Underground.Outbox.Data;
using Underground.OutboxTest.TestHandler;

namespace Underground.OutboxTest;

/// <summary>
/// A context on both sides of the library, which <see cref="TestDbContext"/> is not, so that a test can
/// stage inbox and outbox writes against one interceptor.
/// </summary>
public class InboxOutboxDbContext(DbContextOptions<InboxOutboxDbContext> options) : DbContext(options), IOutboxDbContext, IInboxDbContext
{
    public DbSet<User> Users { get; set; }
    public DbSet<OutboxMessage> OutboxMessages { get; set; }
    public DbSet<InboxMessage> InboxMessages { get; set; }
}
