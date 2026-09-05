using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Npgsql;

using Underground.Outbox.Data;

namespace Underground.Outbox.Domain;

/// <summary>
/// Records that a message was handled. This is the write that ends a message's life; on the outbox it is
/// also the release of the Lease.
/// </summary>
/// <remarks>
/// The write is guarded on the Lease instant the claim granted, so a worker that overran cannot mark a
/// message some other worker now owns. Matching no row is reported and not thrown: the effect has already
/// happened and there is nothing for a caller to recover, which is why this write reports nothing back.
/// On the inbox the guard is trivially satisfied - the row is locked for the whole transaction, so nothing
/// can have moved its visibility instant - and a predicate that is always true is cheaper than giving each
/// side its own write path.
/// </remarks>
internal sealed partial class MarkHandled<TEntity>(
    IDbContext dbContext,
    ILogger<MarkHandled<TEntity>> logger
) where TEntity : class, IMessage
{
    private readonly ILogger<MarkHandled<TEntity>> _logger = logger;

    /// <summary>
    /// Marks the message handled, if this worker still holds it.
    /// </summary>
    internal async Task ExecuteAsync(TEntity message, CancellationToken cancellationToken)
    {
        // clock_timestamp() rather than an instant from here, so that the one column an operator reads to
        // reconstruct what happened is on the same clock as every other instant in the table
        var sql = $"""
            UPDATE {TEntity.TableName}
            SET processed_at = clock_timestamp()
            WHERE id = @id
            AND visible_at = @lease
            """;

        List<NpgsqlParameter> parameters =
        [
            new("id", message.Id),
            new("lease", message.VisibleAt),
        ];

        // S2077: the only value interpolated into the statement is TEntity.TableName, a compile-time
        // constant on the entity type rather than anything a caller supplies (see ADR 0005). Every runtime
        // value below is a parameter.
#pragma warning disable S2077 // Formatting SQL queries is security-sensitive
        var rows = await dbContext.Database
            .ExecuteSqlRawAsync(sql, parameters, cancellationToken)
            .ConfigureAwait(false);
#pragma warning restore S2077

        if (rows == 0)
        {
            LogLeaseLost(message.Id);
        }
    }

    // A warning rather than an exception: the Lease expired, another worker has since claimed the message,
    // and this worker's completion is simply discarded. Nothing is wrong with the system - it is how
    // at-least-once delivery is bounded - but it means an effect was carried out twice, which an operator
    // wants to see the rate of.
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "Lost the Lease on message {MessageId}: it expired before this worker finished, so another worker owns the message and it was not marked handled here")]
    private partial void LogLeaseLost(long messageId);
}
