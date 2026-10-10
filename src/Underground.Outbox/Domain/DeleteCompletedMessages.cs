using Microsoft.EntityFrameworkCore;

using Npgsql;

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
        // the cutoff is taken on the database's clock, which stamped completed_at, so a skewed application
        // clock cannot shorten or lengthen the retention
        var sql = $"""
            DELETE FROM {TEntity.TableName}
            WHERE completed_at < clock_timestamp() - @retention
            """;

        // S2077: the only interpolated value is TEntity.TableName, a compile-time constant (ADR 0005)
#pragma warning disable S2077 // Formatting SQL queries is security-sensitive
        return await _dbContext.Database
            .ExecuteSqlRawAsync(sql, [new NpgsqlParameter("retention", config.CompletedMessageRetention)], cancellationToken)
            .ConfigureAwait(false);
#pragma warning restore S2077
    }
}
