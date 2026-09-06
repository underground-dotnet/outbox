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
/// Guarded on the Lease instant the claim granted, so a worker that overran cannot mark a message some
/// other worker now owns. Matching no row is reported and not thrown - the effect already happened - but
/// it is still the one case in which an effect has certainly been carried out twice, so
/// <see cref="Chain.RecordSuccessStage{TEntity}"/> puts it on the Attempt for the outcome log.
/// On the inbox the guard is trivially satisfied, which is cheaper than a second write path.
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
    /// <returns>
    /// Whether the write landed. <c>false</c> means the Lease was lost - the message is now some other
    /// worker's, which will dispatch it again - and the loss has been logged.
    /// </returns>
    internal async Task<bool> ExecuteAsync(TEntity message, CancellationToken cancellationToken)
    {
        // clock_timestamp(), so this column is on the same clock as every other instant in the table
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

        // S2077: the only interpolated value is TEntity.TableName, a compile-time constant (ADR 0005)
#pragma warning disable S2077 // Formatting SQL queries is security-sensitive
        var rows = await dbContext.Database
            .ExecuteSqlRawAsync(sql, parameters, cancellationToken)
            .ConfigureAwait(false);
#pragma warning restore S2077

        if (rows != 0)
        {
            return true;
        }

        LogLeaseLost(message.Id);
        return false;
    }

    // A warning rather than an exception: nothing is wrong with the system, but the effect was carried
    // out twice, which an operator wants to see the rate of.
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "Lost the Lease on message {MessageId}: it expired before this worker finished, so another worker owns the message and it was not marked handled here")]
    private partial void LogLeaseLost(long messageId);
}
