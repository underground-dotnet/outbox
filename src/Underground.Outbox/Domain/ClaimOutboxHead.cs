using Npgsql;

using Underground.Outbox.Configuration;
using Underground.Outbox.Data;

namespace Underground.Outbox.Domain;

/// <summary>
/// Claims a Head for the outbox by granting itself a Lease: the claim moves
/// <see cref="IMessage.VisibleAt"/> to the Lease expiry, so the message is out of sight for as long as
/// this worker has to finish and comes back on its own if the worker never does. Every later write is
/// guarded on the granted instant, which is what tells an expired worker the message is no longer its own.
/// </summary>
internal sealed class ClaimOutboxHead(
    IDbContext dbContext,
    ServiceConfiguration<OutboxMessage> config
) : ClaimHead<OutboxMessage>(dbContext)
{
    private static readonly string ClaimSql = $"""
        {LockedHeadCte()}
        UPDATE {OutboxMessage.TableName} m
        SET visible_at = clock_timestamp() + @lease
        FROM claimed c
        WHERE m.id = c.id
        RETURNING m.*
        """;

    protected override string Sql => ClaimSql;

    // an interval rather than an instant, so a skewed application clock cannot expire a Lease early
    protected override void AddParameters(List<NpgsqlParameter> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        parameters.Add(new NpgsqlParameter("lease", config.LeaseDuration));
    }
}
