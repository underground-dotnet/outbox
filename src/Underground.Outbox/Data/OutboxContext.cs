using System.CodeDom.Compiler;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Underground.Outbox.Data;

internal sealed class OutboxContext(DbContextOptions options) : DbContext(options)
{
    internal DbSet<OutboxMessage> OutboxMessages { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new OutboxMessageEntityTypeConfiguration());
    }
}
