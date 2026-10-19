using Microsoft.EntityFrameworkCore;

using Npgsql;

using Underground.Outbox.Configuration;
using Underground.Outbox.Data;

namespace Underground.Outbox.Domain;

internal sealed class DeleteCompletedMessages<TEntity>(
    MessageDbContext<TEntity> messageDbContext,
    ServiceConfiguration<TEntity> config,
    string? inbox = null
) where TEntity : class, IMessage
{
    private readonly IDbContext _dbContext = messageDbContext.Context;

    internal async Task<int> ExecuteAsync(CancellationToken cancellationToken)
    {
        List<NpgsqlParameter> parameters = [new NpgsqlParameter("retention", config.CompletedMessageRetention)];

        // each Inbox keeps its own retention, so a sweep removes only its own Inbox's rows
        var inScope = "";
        if (inbox is not null)
        {
            inScope = "AND inbox = @inbox";
            parameters.Add(new NpgsqlParameter("inbox", inbox));
        }

        // the cutoff is taken on the database's clock, which stamped completed_at, so a skewed application
        // clock cannot shorten or lengthen the retention
        var sql = $"""
            DELETE FROM {TEntity.TableName}
            WHERE completed_at < clock_timestamp() - @retention
            {inScope}
            """;

        // S2077: the only interpolated values are TEntity.TableName, a compile-time constant (ADR 0005), and a
        // literal predicate
#pragma warning disable S2077 // Formatting SQL queries is security-sensitive
        return await _dbContext.Database
            .ExecuteSqlRawAsync(sql, parameters, cancellationToken)
            .ConfigureAwait(false);
#pragma warning restore S2077
    }
}
