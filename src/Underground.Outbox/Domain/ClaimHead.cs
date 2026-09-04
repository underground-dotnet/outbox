using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

using System.Runtime.CompilerServices;

using Npgsql;

using Underground.Outbox.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging;
using System.Data;

namespace Underground.Outbox.Domain;

internal abstract partial class ClaimHead<TEntity>(IDbContext dbContext, ILogger<ClaimHead<TEntity>> logger) where TEntity : class, IMessage
{
#pragma warning disable S2743 // A static field in a generic type is not shared among instances of different close constructed types.
    private static readonly ConditionalWeakTable<IModel, string> SqlByModel = [];
#pragma warning restore S2743 // A static field in a generic type is not shared among instances of different close constructed types.

    /// <summary>
    /// Claims the Group's Head - its oldest settled unhandled message - and locks it for the calling
    /// transaction. Returns <c>null</c> when the Group has no Head, when its Head is not yet visible, and
    /// when another worker already holds it.
    /// </summary>
    internal async Task<TEntity?> ExecuteAsync(string groupKey, CancellationToken cancellationToken)
    {
        var sql = SqlByModel.GetValue(dbContext.Model, static model => BuildSql(model));

        var connection = dbContext.Database.GetDbConnection();
        var needsOpen = connection.State != ConnectionState.Open;

        if (needsOpen)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            var cmd = connection.CreateCommand();
            await using (cmd.ConfigureAwait(false))
            {
                cmd.CommandText = sql;
                cmd.Parameters.Add(new NpgsqlParameter("groupKey", groupKey));

                LogClaimSql(groupKey, sql);
                var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                await using (reader.ConfigureAwait(false))
                {
                    return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
                        ? BuildEntityFromReader(reader)
                        : null;
                }
            }
        }
        finally
        {
            if (needsOpen)
            {
                await connection.CloseAsync().ConfigureAwait(false);
            }
        }
    }

    private static string BuildSql(IModel model)
    {
        // table and column names are read off the model rather than written literally, since a consumer
        // can remap any of them through EF Core mappings
        var table = MessageTable.For<TEntity>(model);

        // Head discovery is deliberately in two stages. The Head is the lowest (transaction_id, id) among
        // the Group's settled unhandled rows *regardless of visibility*, and only then is visibility tested
        // against that one row. Filtering by visible_at first would hand out the message behind a Head that
        // is in backoff or scheduled for later, which is exactly the reordering this design exists to
        // prevent. A Group whose Head is invisible therefore offers nothing at all.
        //
        // Ordering is by (transaction_id, id) rather than by id alone: identity values are handed out when a
        // row is inserted, not when its transaction commits, so a transaction that starts later but commits
        // first would otherwise have its message handled first.
        //
        // The settled filter withholds a message until no still-running transaction could yet insert an
        // earlier one into its Group. It is safe to apply it during Head discovery only because the sort key
        // is (transaction_id, id): an unsettled row always sorts after every settled one, so excluding it can
        // never promote a later message ahead of it. With id alone this would be a bug.
        //
        // FOR UPDATE cannot be applied to a WITH query, so the locking clause names the outer join back to
        // the table. It is SKIP LOCKED rather than NOWAIT: a Head another worker already holds is simply not
        // offered, instead of aborting the statement.
        //
        // Both instants are compared against clock_timestamp() rather than now(), which is frozen for the
        // transaction that the inbox holds open across its handler.
        return $"""
            WITH head AS (
                SELECT {table.Id}
                FROM {table.Name}
                WHERE {table.ProcessedAt} IS NULL
                AND {table.GroupKey} = @groupKey
                AND {table.TransactionId} < pg_snapshot_xmin(pg_current_snapshot())
                ORDER BY {table.TransactionId}, {table.Id}
                LIMIT 1
            )
            SELECT m.{table.Id}, m.{table.EventId}, m.{table.TransactionId}, m.{table.CreatedAt}, m.{table.Type}, m.{table.GroupKey}, m.{table.Data}, m.{table.RetryCount}, m.{table.VisibleAt}, m.{table.ProcessedAt}
            FROM head h
            JOIN {table.Name} m ON m.{table.Id} = h.{table.Id}
            WHERE m.{table.ProcessedAt} IS NULL
            AND m.{table.VisibleAt} <= clock_timestamp()
            FOR UPDATE OF m SKIP LOCKED
            """;
    }

    protected abstract TEntity BuildEntityFromReader(DbDataReader reader);

    [LoggerMessage(
            EventId = 1,
            Level = LogLevel.Information,
            Message = "Executing SQL to claim the Head of group {GroupKey}: {Sql}")]
    private partial void LogClaimSql(string GroupKey, string Sql);
}
