using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

using System.Runtime.CompilerServices;

using Npgsql;

using Underground.Outbox.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging;
using System.Data;

namespace Underground.Outbox.Domain;

internal abstract partial class FetchMessages<TEntity>(IDbContext dbContext, ILogger<FetchMessages<TEntity>> logger) where TEntity : class, IMessage
{
#pragma warning disable S2743 // A static field in a generic type is not shared among instances of different close constructed types.
    private static readonly ConditionalWeakTable<IModel, string> SqlByModel = [];
#pragma warning restore S2743 // A static field in a generic type is not shared among instances of different close constructed types.

    internal async Task<List<TEntity>> ExecuteAsync(string groupKey, int batchSize, CancellationToken cancellationToken)
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
                cmd.Parameters.Add(new NpgsqlParameter("batchSize", batchSize));

                var result = new List<TEntity>();

                LogFetchSql(groupKey, sql);
                var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                await using (reader.ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        result.Add(BuildEntityFromReader(reader));
                    }
                }

                return result;
            }
        }
        catch (PostgresException ex) when (string.Equals(ex.SqlState, "55P03", StringComparison.Ordinal)) // lock_not_available
        {
            // another processor is already handling messages for this group
            LogCouldNotAcquireLock(typeof(TEntity).Name, groupKey, ex);
            return [];
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

        // Ordering is by (transaction_id, id) rather than by id alone: identity values are handed out when a
        // row is inserted, not when its transaction commits, so a transaction that starts later but commits
        // first would otherwise have its message handled first.
        //
        // The settled filter withholds a message until no still-running transaction could yet insert an
        // earlier one into its Group. It is safe to apply it here, before ordering, only because the sort key
        // is (transaction_id, id): an unsettled row always sorts after every settled one, so excluding it can
        // never promote a later message ahead of it. With id alone this would be a bug.
        //
        // Visibility is compared against clock_timestamp() rather than now(), which is frozen for the
        // transaction that the inbox holds open across its handler.
        return $"""
            SELECT {table.Id}, {table.EventId}, {table.TransactionId}, {table.CreatedAt}, {table.Type}, {table.GroupKey}, {table.Data}, {table.RetryCount}, {table.VisibleAt}, {table.ProcessedAt}
            FROM {table.Name}
            WHERE {table.ProcessedAt} IS NULL
            AND {table.GroupKey} = @groupKey
            AND {table.TransactionId} < pg_snapshot_xmin(pg_current_snapshot())
            AND {table.VisibleAt} <= clock_timestamp()
            ORDER BY {table.TransactionId}, {table.Id}
            LIMIT @batchSize
            FOR UPDATE NOWAIT
            """;
    }

    protected abstract TEntity BuildEntityFromReader(DbDataReader reader);

    [LoggerMessage(
            EventId = 1,
            Level = LogLevel.Information,
            Message = "Executing SQL to fetch messages for group {GroupKey}: {Sql}")]
    private partial void LogFetchSql(string GroupKey, string Sql);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Debug,
        Message = "Could not acquire lock for {Type} group {GroupKey}, skipping processing")]
    private partial void LogCouldNotAcquireLock(string Type, string GroupKey, Exception exception);
}
