using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Underground.Outbox.Data;

/// <summary>
/// Represents a database context that contains the outbox messages.
/// </summary>
public interface IOutboxDbContext : IAsyncDisposable
{
    /// <summary>
    /// Gets or sets the set of outbox messages.
    /// </summary>
    DbSet<OutboxMessage> OutboxMessages { get; set; }

    public DatabaseFacade Database { get; }
    public ChangeTracker ChangeTracker { get; }
#pragma warning disable CA1716 // Identifiers should not match keywords
    public DbSet<TEntity> Set<TEntity>() where TEntity : class;
#pragma warning restore CA1716 // Identifiers should not match keywords

    /// <summary>
    /// Asynchronously saves all changes made in this context to the database.
    /// </summary>
    /// <param name="cancellationToken">A <see cref="CancellationToken" /> to observe while waiting for the task to complete.</param>
    /// <returns>
    /// A task that represents the asynchronous save operation. The task result contains the
    /// number of state entries written to the database.
    /// </returns>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
