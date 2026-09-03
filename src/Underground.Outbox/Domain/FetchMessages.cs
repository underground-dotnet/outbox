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
        // dynamically extract table and column names to build the SQL query, since those can be overriden via EF Core mappings
        var entityType = model.FindEntityType(typeof(TEntity)) ?? throw new InvalidOperationException($"Entity type {typeof(TEntity)} not found in DbContext model.");
        var tableName = entityType.GetTableName() ?? throw new InvalidOperationException($"Table name for entity type {typeof(TEntity)} is not configured.");
        var schema = entityType.GetSchema();
        var fullTableName = string.IsNullOrEmpty(schema) ? $"\"{tableName}\"" : $"\"{schema}\".\"{tableName}\"";
        var tableIdentifier = StoreObjectIdentifier.Table(tableName, schema);

        var idColumn = entityType.FindProperty(nameof(IMessage.Id))?.GetColumnName(tableIdentifier)
            ?? throw new InvalidOperationException($"Property {nameof(IMessage.Id)} not found in entity type {typeof(TEntity)}.");
        var eventIdColumn = entityType.FindProperty(nameof(IMessage.EventId))?.GetColumnName(tableIdentifier)
            ?? throw new InvalidOperationException($"Property {nameof(IMessage.EventId)} not found in entity type {typeof(TEntity)}.");
        var transactionIdColumn = entityType.FindProperty(nameof(IMessage.TransactionId))?.GetColumnName(tableIdentifier)
            ?? throw new InvalidOperationException($"Property {nameof(IMessage.TransactionId)} not found in entity type {typeof(TEntity)}.");
        var createdAtColumn = entityType.FindProperty(nameof(IMessage.CreatedAt))?.GetColumnName(tableIdentifier)
            ?? throw new InvalidOperationException($"Property {nameof(IMessage.CreatedAt)} not found in entity type {typeof(TEntity)}.");
        var typeColumn = entityType.FindProperty(nameof(IMessage.Type))?.GetColumnName(tableIdentifier)
            ?? throw new InvalidOperationException($"Property {nameof(IMessage.Type)} not found in entity type {typeof(TEntity)}.");
        var groupKeyColumn = entityType.FindProperty(nameof(IMessage.GroupKey))?.GetColumnName(tableIdentifier)
            ?? throw new InvalidOperationException($"Property {nameof(IMessage.GroupKey)} not found in entity type {typeof(TEntity)}.");
        var dataColumn = entityType.FindProperty(nameof(IMessage.Data))?.GetColumnName(tableIdentifier)
            ?? throw new InvalidOperationException($"Property {nameof(IMessage.Data)} not found in entity type {typeof(TEntity)}.");
        var retryCountColumn = entityType.FindProperty(nameof(IMessage.RetryCount))?.GetColumnName(tableIdentifier)
            ?? throw new InvalidOperationException($"Property {nameof(IMessage.RetryCount)} not found in entity type {typeof(TEntity)}.");
        var processedAtColumn = entityType.FindProperty(nameof(IMessage.ProcessedAt))?.GetColumnName(tableIdentifier)
            ?? throw new InvalidOperationException($"Property {nameof(IMessage.ProcessedAt)} not found in entity type {typeof(TEntity)}.");

        // Ordering is by (transaction_id, id) rather than by id alone: identity values are handed out when a
        // row is inserted, not when its transaction commits, so a transaction that starts later but commits
        // first would otherwise have its message handled first.
        //
        // The settled filter withholds a message until no still-running transaction could yet insert an
        // earlier one into its Group. It is safe to apply it here, before ordering, only because the sort key
        // is (transaction_id, id): an unsettled row always sorts after every settled one, so excluding it can
        // never promote a later message ahead of it. With id alone this would be a bug.
        return $"""
            SELECT "{idColumn}", "{eventIdColumn}", "{transactionIdColumn}", "{createdAtColumn}", "{typeColumn}", "{groupKeyColumn}", "{dataColumn}", "{retryCountColumn}", "{processedAtColumn}"
            FROM {fullTableName}
            WHERE "{processedAtColumn}" IS NULL
            AND "{groupKeyColumn}" = @groupKey
            AND "{transactionIdColumn}" < pg_snapshot_xmin(pg_current_snapshot())
            ORDER BY "{transactionIdColumn}", "{idColumn}"
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
