using Microsoft.Extensions.Logging;

using Underground.Outbox.Data;

namespace Underground.Outbox.Domain;

/// <summary>
/// Records that a message was handled. This is the write that ends a message's life; on the outbox it is
/// also the release of the Lease.
/// </summary>
internal sealed class MarkHandled<TEntity>(
    IDbContext dbContext,
    ILogger<MarkHandled<TEntity>> logger
) : GuardedWrite<TEntity>(dbContext, logger) where TEntity : class, IMessage
{
    // clock_timestamp() rather than an instant from here, so that the one column an operator reads to
    // reconstruct what happened is on the same clock as every other instant in the table
    protected override string BuildSql(MessageTable table) => $"""
        UPDATE {table.Name}
        SET {table.ProcessedAt} = clock_timestamp()
        {Guard(table)}
        """;
}
