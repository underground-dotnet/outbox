using Microsoft.EntityFrameworkCore;

using Underground.Outbox.Data;

namespace Underground.OutboxTest;

/// <summary>A neighbouring module's context, in a schema of its own.</summary>
public class ModuleBDbContext(DbContextOptions<ModuleBDbContext> options) : DbContext(options), IOutboxDbContext
{
    /// <summary>This module's Outbox Messages, in the <c>module_b</c> schema.</summary>
    public DbSet<OutboxMessage> OutboxMessages { get; set; }

    /// <summary>The schema this module's tables live in, stated here and again at registration (ADR 0007).</summary>
    public const string Schema = "module_b";

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);
        base.OnModelCreating(modelBuilder);
    }
}
