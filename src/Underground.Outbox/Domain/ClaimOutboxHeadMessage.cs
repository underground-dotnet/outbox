using Microsoft.Extensions.Logging;

using Npgsql;

using Underground.Outbox.Configuration;
using Underground.Outbox.Data;

namespace Underground.Outbox.Domain;

/// <summary>
/// Claims a HeadMessage for the outbox by granting itself a Lease: the claim moves
/// <see cref="IMessage.VisibleAt"/> to the Lease expiry, so the message is out of sight for as long as
/// this worker has to finish and comes back on its own if the worker never does. The <see cref="Claim{TEntity}"/>
/// guards every later write on the granted instant, which is what tells an expired worker the message is no
/// longer its own.
/// </summary>
internal sealed class ClaimOutboxHeadMessage(
    IOutboxDbContext dbContext,
    ServiceConfiguration<OutboxMessage> config,
    ILoggerFactory loggerFactory
) : ClaimHeadMessage<OutboxMessage>(dbContext, loggerFactory)
{
    private static readonly string ClaimStatement = BuildStatement($"""
        UPDATE {OutboxMessage.TableName} m
        SET visible_at = clock_timestamp() + @lease
        FROM claimed c
        WHERE m.id = c.id
        RETURNING m.*
        """, "group_key");

    protected override string Statement => ClaimStatement;

    // an interval rather than an instant, so a skewed application clock cannot expire a Lease early
    protected override void AddParameters(List<NpgsqlParameter> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        parameters.Add(new NpgsqlParameter("lease", config.LeaseDuration));
    }
}
