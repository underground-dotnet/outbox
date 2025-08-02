using Microsoft.EntityFrameworkCore;

using Npgsql;

using Underground.Outbox.Configuration;
using Underground.Outbox.Data;
using Underground.Outbox.Exceptions;

namespace Underground.Outbox.Domain;

internal sealed class AddMessageToOutbox(OutboxServiceConfiguration config)
{
    public async Task ExecuteAsync(DbContext context, OutboxMessage message, CancellationToken cancellationToken = default)
    {
        if (!HasActiveTransaction(context))
        {
            throw new NoActiveTransactionException();
        }

        var sql = $@"INSERT INTO {config.FullTableName} (trace_id, occurred_on, type, partition_key, data)
                            VALUES (@trace_id, @occurred_on, @type, @partition_key, @data)";
        IEnumerable<NpgsqlParameter> sqlParams = [
            new NpgsqlParameter("@trace_id", message.TraceId),
            new NpgsqlParameter("@occurred_on", message.OccurredOn),
            new NpgsqlParameter("@type", message.Type),
            new NpgsqlParameter("@partition_key", message.PartitionKey),
            new NpgsqlParameter("@data", message.Data)
        ];

        // using raw sql is required, since the schema + table name cannot be parameterized in EF Core and is coming from the configuration
#pragma warning disable EF1002 // Risk of vulnerability to SQL injection.
        await context.Database.ExecuteSqlRawAsync(sql, sqlParams, cancellationToken: cancellationToken);
#pragma warning restore EF1002 // Risk of vulnerability to SQL injection.
    }

    private static bool HasActiveTransaction(DbContext context)
    {
        return context.Database.CurrentTransaction != null;
    }
}
