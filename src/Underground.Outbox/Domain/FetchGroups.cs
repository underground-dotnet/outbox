using Microsoft.EntityFrameworkCore;

using Underground.Outbox.Data;

namespace Underground.Outbox.Domain;

internal sealed class FetchGroups<TEntity>(IDbContext dbContext) where TEntity : class, IMessage
{
    internal async Task<IEnumerable<string>> ExecuteAsync(CancellationToken cancellationToken)
    {
        return await dbContext.Set<TEntity>()
                    .Where(message => message.ProcessedAt == null)
                    .Select(message => message.GroupKey)
                    .Distinct()
                    .AsNoTracking()
                    .ToListAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}
