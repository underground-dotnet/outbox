using Microsoft.EntityFrameworkCore;

using Underground.Outbox.Data;

namespace Underground.OutboxTest;

/// <summary>
/// One module's context, in its own schema, so that a test can register two outboxes against one
/// connection string and watch each Claim only from its own tables.
/// </summary>
public class ModuleADbContext(DbContextOptions<ModuleADbContext> options) : DbContext(options), IOutboxDbContext, IInboxDbContext
{
    /// <summary>This module's Outbox Messages, in the <c>module_a</c> schema.</summary>
    public DbSet<OutboxMessage> OutboxMessages { get; set; }

    /// <summary>This module's Inbox Messages, in the <c>module_a</c> schema.</summary>
    public DbSet<InboxMessage> InboxMessages { get; set; }

    /// <summary>The schema this module's tables live in, stated here and again at registration (ADR 0007).</summary>
    public const string Schema = "module_a";

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);
        base.OnModelCreating(modelBuilder);
    }
}
