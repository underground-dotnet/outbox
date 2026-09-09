using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Npgsql;

using Underground.Outbox.Configuration;
using Underground.Outbox.Data;

namespace Underground.Outbox.Domain;

/// <summary>
/// Records that a message was completed. This is the write that ends a message's life; on the outbox it is
/// also the release of the Lease.
/// </summary>
/// <remarks>
/// Guarded on the Lease instant the claim granted, so a worker that overran cannot mark a message some
/// other worker now owns. Matching no row is reported and not thrown - the effect already happened - but
/// it is still the one case in which an effect has certainly been carried out twice, so
/// <see cref="Middleware.RecordSuccessMiddleware{TContext, TEntity}"/> puts it on the ProcessingAttempt for the outcome log.
/// On the inbox the guard is trivially satisfied, which is cheaper than a second write path.
/// </remarks>
internal sealed partial class MarkCompleted<TContext, TEntity>(
    TContext dbContext,
    ServiceConfiguration<TContext, TEntity> config,
    ILogger<MarkCompleted<TContext, TEntity>> logger
)
    where TContext : DbContext
    where TEntity : class, IMessage
{
    private readonly ILogger<MarkCompleted<TContext, TEntity>> _logger = logger;

    // clock_timestamp(), so this column is on the same clock as every other instant in the table
    private readonly string _sql = StatementCache.GetOrAdd(config.QualifiedTable, "complete", qualifiedTable => $"""
        UPDATE {qualifiedTable}
        SET completed_at = clock_timestamp()
        WHERE id = @id
        AND visible_at = @lease
        """);

    /// <summary>
    /// Marks the message handled, if this worker still holds it.
    /// </summary>
    /// <returns>
    /// Whether the write landed. <c>false</c> means the Lease was lost - the message is now some other
    /// worker's, which will dispatch it again - and the loss has been logged.
    /// </returns>
    internal async Task<bool> ExecuteAsync(TEntity message, CancellationToken cancellationToken)
    {
        List<NpgsqlParameter> parameters =
        [
            new("id", message.Id),
            new("lease", message.VisibleAt),
        ];

        // S2077: the statement is composed from the table name (a compile-time constant, ADR 0005) and the
        // schema given at registration, never from anything a message carries
#pragma warning disable S2077 // Formatting SQL queries is security-sensitive
        var rows = await dbContext.Database
            .ExecuteSqlRawAsync(_sql, parameters, cancellationToken)
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
        Message = "Lost the Lease on message {MessageId}: it expired before this worker finished, so another worker owns the message and it was not marked completed here")]
    private partial void LogLeaseLost(long messageId);
}
