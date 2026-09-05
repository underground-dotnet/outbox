using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Npgsql;

using Underground.Outbox.Configuration;
using Underground.Outbox.Data;

namespace Underground.Outbox.Domain;

/// <summary>
/// Records a failed attempt and pushes the message out of sight for the backoff delay, so that a
/// handler that keeps failing is not retried in a hot loop. On the outbox this write is also the release
/// of the Lease.
/// </summary>
/// <remarks>
/// The write is guarded on the Lease instant the claim granted, which is what makes it the probe that
/// establishes this worker still holds the message: unlike marking a message handled, the answer here has
/// a consumer, because consumer exception policies must not run against a message some other worker now
/// owns. On the inbox the guard is trivially satisfied - the row is locked for the whole transaction, so
/// nothing can have moved its visibility instant - and a predicate that is always true is cheaper than
/// giving each side its own write path.
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
        // The application supplies an interval and never an instant, so the new visibility is anchored to
        // the database's clock: an instance whose own clock is skewed cannot bring a message back early or
        // late.
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

        // S2077: the only value interpolated into the statement is TEntity.TableName, a compile-time
        // constant on the entity type rather than anything a caller supplies (see ADR 0005). Every runtime
        // value below is a parameter.
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

    // A warning rather than an exception: the Lease expired, another worker has since claimed the message,
    // and this worker's attempt bookkeeping is simply discarded. Nothing is wrong with the system - it is
    // how at-least-once delivery is bounded - but it means an effect was carried out twice, which an
    // operator wants to see the rate of.
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "Lost the Lease on message {MessageId}: it expired before this worker finished, so another worker owns the message and this attempt was not recorded")]
    private partial void LogLeaseLost(long messageId);
}
