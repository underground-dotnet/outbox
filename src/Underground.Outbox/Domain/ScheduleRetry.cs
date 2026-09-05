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
internal sealed class ScheduleRetry<TEntity>(
    IDbContext dbContext,
    ServiceConfiguration<TEntity> config,
    ILogger<ScheduleRetry<TEntity>> logger
) : GuardedWrite<TEntity>(dbContext, logger) where TEntity : class, IMessage
{
    private readonly RetryBackoff _backoff = new(config.BackoffBase, config.MaxBackoff, config.BackoffJitter);

    // The application supplies an interval and never an instant, so the new visibility is anchored to the
    // database's clock: an instance whose own clock is skewed cannot bring a message back early or late.
    protected override string Sql => $"""
        UPDATE {TEntity.TableName}
        SET retry_count = retry_count + 1,
            visible_at = clock_timestamp() + @delay
        {Guard}
        """;

    protected override void AddParameters(List<NpgsqlParameter> parameters, TEntity message)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(message);

        parameters.Add(new NpgsqlParameter("delay", _backoff.DelayFor(message.RetryCount)));
    }
}
