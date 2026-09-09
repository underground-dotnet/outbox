using Microsoft.EntityFrameworkCore;

using Underground.Outbox.Configuration;
using Underground.Outbox.Data;

namespace Underground.Outbox.Domain;

internal sealed class DeleteCompletedMessages<TContext, TEntity>(
    TContext dbContext,
    ServiceConfiguration<TContext, TEntity> config
)
    where TContext : DbContext
    where TEntity : class, IMessage
{
    internal async Task<int> ExecuteAsync(CancellationToken cancellationToken)
    {
        var cutoff = DateTime.UtcNow - config.CompletedMessageRetention;

        return await dbContext.Set<TEntity>()
            .Where(message => message.CompletedAt != null && message.CompletedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
