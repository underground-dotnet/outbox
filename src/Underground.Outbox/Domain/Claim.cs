using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Npgsql;

using Underground.Outbox.Data;

namespace Underground.Outbox.Domain;

/// <summary>
/// A worker holding one HeadMessage, and the only way to write about it while holding it. Every write is
/// guarded on the Lease instant this Claim holds, so a worker that overran cannot overwrite a newer Claim.
/// </summary>
/// <remarks>
/// The outbox holds a Lease, which can expire. The inbox holds the row lock, which keeps the guard
/// satisfied - cheaper than a second write path - so a miss there means the claim handed out some other
/// version of the row, and <see cref="InboxProcessor"/> rolls the attempt back.
/// </remarks>
internal sealed partial class Claim<TEntity> where TEntity : class, IMessage
{
    // the one predicate every guarded write shares; @lease is bound from _lease
    private const string GuardedById = "WHERE id = @id AND visible_at = @lease";

    private readonly IDbContext _dbContext;
    private readonly ILogger<Claim<TEntity>> _logger;

    // Moved only by a renewal, which RenewLeaseMiddleware has stopped and awaited before any outcome write
    // reads it, so the field is never written and read concurrently.
    private DateTime _lease;

    internal Claim(TEntity message, IDbContext dbContext, ILogger<Claim<TEntity>> logger)
    {
        Message = message;
        _dbContext = dbContext;
        _logger = logger;
        _lease = message.VisibleAt;
    }

    /// <summary>
    /// The claimed message, as the claim statement returned it. Its <see cref="IMessage.VisibleAt"/> is the
    /// Lease as granted and goes stale once the Lease is renewed.
    /// </summary>
    internal TEntity Message { get; }

    /// <summary>
    /// Marks the message completed. On the outbox this also releases the Lease.
    /// </summary>
    /// <returns>
    /// Whether the Claim was still held. <c>false</c> means another worker owns the message and will
    /// dispatch it again; the loss has been logged.
    /// </returns>
    internal async Task<bool> TryCompleteAsync(CancellationToken cancellationToken)
    {
        // clock_timestamp(), so this column is on the same clock as every other instant in the table
        var sql = $"""
            UPDATE {TEntity.TableName}
            SET completed_at = clock_timestamp()
            {GuardedById}
            """;

        return await TryWriteAsync(sql, [], "completion", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Records a failed attempt and pushes the message out of sight for <paramref name="delay"/>. On the
    /// outbox this also releases the Lease.
    /// </summary>
    /// <returns>
    /// Whether the Claim was still held. <c>false</c> means another worker owns the message, nothing here may
    /// touch it, and the loss has been logged.
    /// </returns>
    internal async Task<bool> TryRecordFailureAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        // an interval and never an instant, so a skewed application clock cannot bring a message back
        // early or late
        var sql = $"""
            UPDATE {TEntity.TableName}
            SET retry_count = retry_count + 1,
                visible_at = clock_timestamp() + @delay
            {GuardedById}
            """;

        return await TryWriteAsync(sql, [new("delay", delay)], "failed attempt", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Moves the Lease expiry to <paramref name="extension"/> from now, on <paramref name="connection"/>
    /// rather than the Claim's own context, which the Handler may be using at that moment. See ADR 0011.
    /// </summary>
    /// <returns>Whether the Claim was still held. <c>false</c> has been logged.</returns>
    internal async Task<bool> TryRenewAsync(IDbContext connection, TimeSpan extension, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);

        await connection.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        var command = connection.Database.GetDbConnection().CreateCommand();
        await using (command.ConfigureAwait(false))
        {
            // an interval rather than an instant, so a skewed application clock cannot expire the Lease early.
            // S2077: the only interpolated values are TEntity.TableName and the guard, both constants (ADR 0005)
#pragma warning disable S2077 // Formatting SQL queries is security-sensitive
            command.CommandText = $"""
                UPDATE {TEntity.TableName}
                SET visible_at = clock_timestamp() + @extension
                {GuardedById}
                RETURNING visible_at
                """;
#pragma warning restore S2077
            command.Parameters.AddRange(GuardParameters());
            command.Parameters.Add(new NpgsqlParameter("extension", extension));

            if (await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not DateTime renewed)
            {
                LogLeaseLost(Message.Id, "renewal");
                return false;
            }

            _lease = renewed;
            return true;
        }
    }

    private async Task<bool> TryWriteAsync(string sql, List<NpgsqlParameter> parameters, string outcome, CancellationToken cancellationToken)
    {
        parameters.AddRange(GuardParameters());

        // S2077: the only interpolated values are TEntity.TableName and the guard, both constants (ADR 0005)
#pragma warning disable S2077 // Formatting SQL queries is security-sensitive
        var rows = await _dbContext.Database
            .ExecuteSqlRawAsync(sql, parameters, cancellationToken)
            .ConfigureAwait(false);
#pragma warning restore S2077

        if (rows != 0)
        {
            return true;
        }

        LogLeaseLost(Message.Id, outcome);
        return false;
    }

    // fresh per statement: an NpgsqlParameter belongs to one command at a time
    private NpgsqlParameter[] GuardParameters() => [new("id", Message.Id), new("lease", _lease)];

    // A warning rather than an exception: nothing is wrong with the system, but an effect may now be carried
    // out twice, which an operator wants to see the rate of.
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "Lost the Lease on message {MessageId}: it expired before this worker finished, so another worker owns the message and this worker's {Outcome} was not recorded")]
    private partial void LogLeaseLost(long messageId, string outcome);
}
