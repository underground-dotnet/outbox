using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Npgsql;

using Underground.Outbox.Configuration;
using Underground.Outbox.Data;

namespace Underground.Outbox.Domain;

/// <summary>
/// Records a failed attempt and pushes the message out of sight for the backoff delay, so a handler that
/// keeps failing is not retried in a hot loop. On the outbox this write also releases the Lease.
/// </summary>
/// <remarks>
/// Guarded on the Lease instant the claim granted, which makes it the probe establishing that this worker
/// still holds the message - consumer exception policies must not run against a message some other worker
/// now owns. On the inbox the guard is trivially satisfied, which is cheaper than a second write path.
/// </remarks>
internal sealed partial class ScheduleRetry<TEntity>(
    IDbContext dbContext,
    ServiceConfiguration<TEntity> config,
    ILogger<ScheduleRetry<TEntity>> logger
) where TEntity : class, IMessage
{
    private readonly RetryBackoff _backoff = new(config.BackoffBase, config.MaxBackoff, config.BackoffJitter);
    private readonly ILogger<ScheduleRetry<TEntity>> _logger = logger;

    /// <summary>
    /// Records the attempt, if this worker still holds the message.
    /// </summary>
    /// <returns>
    /// Whether the write landed. <c>false</c> means the Lease was lost - the message is now some other
    /// worker's, nothing here may touch it, and the loss has been logged.
    /// </returns>
    internal async Task<bool> ExecuteAsync(TEntity message, CancellationToken cancellationToken)
    {
        // an interval and never an instant, so a skewed application clock cannot bring a message back
        // early or late
        var sql = $"""
            UPDATE {TEntity.TableName}
            SET retry_count = retry_count + 1,
                visible_at = clock_timestamp() + @delay
            WHERE id = @id
            AND visible_at = @lease
            """;

        List<NpgsqlParameter> parameters =
        [
            new("id", message.Id),
            new("lease", message.VisibleAt),
            new("delay", _backoff.DelayFor(message.RetryCount)),
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
        Message = "Lost the Lease on message {MessageId}: it expired before this worker finished, so another worker owns the message and this attempt was not recorded")]
    private partial void LogLeaseLost(long messageId);
}
