using Microsoft.EntityFrameworkCore;

using Underground.Outbox.Data;

namespace Underground.Outbox.Domain;

internal sealed class FetchPartitions<TEntity>(IDbContext dbContext) where TEntity : class, IMessage
{
    internal async Task<IEnumerable<string>> ExecuteAsync(CancellationToken cancellationToken)
    {
        return await dbContext.Set<TEntity>()
                    .Where(message => message.ProcessedAt == null)
                    .Select(message => message.PartitionKey)
                    .Distinct()
                    .AsNoTracking()
                    .ToListAsync(cancellationToken: cancellationToken);
    }
}
