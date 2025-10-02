using Microsoft.EntityFrameworkCore;

namespace Underground.Outbox.Data;

/// <summary>
/// Represents a database context that contains the outbox messages.
/// </summary>
public interface IOutboxDbContext
{
    /// <summary>
    /// Gets or sets the set of outbox messages.
    /// </summary>
    DbSet<OutboxMessage> OutboxMessages { get; set; }

    /// <summary>
    /// Asynchronously saves all changes made in this context to the database.
    /// </summary>
    /// <param name="cancellationToken">A <see cref="CancellationToken" /> to observe while waiting for the task to complete.</param>
    /// <returns>
    /// A task that represents the asynchronous save operation. The task result contains the
    /// number of state entries written to the database.
    /// </returns>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    // protected override void OnModelCreating(ModelBuilder modelBuilder)
    // {
    //     modelBuilder.ApplyConfiguration(new OutboxMessageEntityTypeConfiguration());
    // }
}

public static class DbContextExtensions
{
    public static string GetTableName<T>(this DbContext context) where T : class
    {
        var entityType = context.Model.FindEntityType(typeof(OutboxMessage)) ?? throw new InvalidOperationException($"Entity type {typeof(T).Name} not found in model");
        var schema = entityType.GetSchema();
        var tableName = entityType.GetTableName() ?? throw new InvalidOperationException($"Table name for entity type {typeof(T).Name} not found in model");
        return schema != null ? $"{schema}.{tableName}" : tableName;
    }
}
