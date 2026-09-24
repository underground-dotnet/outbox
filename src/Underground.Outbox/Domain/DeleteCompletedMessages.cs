using Microsoft.EntityFrameworkCore;

using Underground.Outbox.Configuration;
using Underground.Outbox.Data;

namespace Underground.Outbox.Domain;

internal sealed class DeleteCompletedMessages<TEntity>(
    MessageDbContext<TEntity> messageDbContext,
    ServiceConfiguration<TEntity> config
) where TEntity : class, IMessage
{
    private readonly IDbContext _dbContext = messageDbContext.Context;

    internal async Task<int> ExecuteAsync(CancellationToken cancellationToken)
    {
        var cutoff = DateTime.UtcNow - config.CompletedMessageRetention;

        return await _dbContext.Set<TEntity>()
            .Where(message => message.CompletedAt != null && message.CompletedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
