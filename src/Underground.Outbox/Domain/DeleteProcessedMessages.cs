using Microsoft.EntityFrameworkCore;

using Underground.Outbox.Configuration;
using Underground.Outbox.Data;

namespace Underground.Outbox.Domain;

internal sealed class DeleteProcessedMessages<TEntity>(
    IDbContext dbContext,
    ServiceConfiguration<TEntity> config
) where TEntity : class, IMessage
{
    internal async Task<int> ExecuteAsync(CancellationToken cancellationToken)
    {
        var cutoff = DateTime.UtcNow - config.ProcessedMessageRetention;

        return await dbContext.Set<TEntity>()
            .Where(message => message.ProcessedAt != null && message.ProcessedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
