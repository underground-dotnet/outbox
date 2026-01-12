using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Underground.Outbox.Data;

public interface IDbContext : IAsyncDisposable
{
    public DatabaseFacade Database { get; }
    public ChangeTracker ChangeTracker { get; }
#pragma warning disable CA1716 // Identifiers should not match keywords
    public DbSet<TEntity> Set<TEntity>() where TEntity : class;
#pragma warning restore CA1716 // Identifiers should not match keywords

    public IModel Model { get; }

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
