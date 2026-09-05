using Npgsql;

using Underground.Outbox.Configuration;
using Underground.Outbox.Data;

namespace Underground.Outbox.Domain;

/// <summary>
/// Claims a Head for the outbox by granting itself a Lease on it: the claim moves
/// <see cref="IMessage.VisibleAt"/> to the Lease expiry, so the message is out of sight for as long as
/// this worker has to finish, and comes back on its own if the worker never does.
/// </summary>
/// <remarks>
/// The granted instant is returned rather than computed here, and every later write to the message is
/// guarded on it. That is what tells a worker whose Lease expired that the message is no longer its own.
/// </remarks>
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

    // an interval rather than an instant, so that the expiry is computed by the database: an instance with
    // a skewed clock cannot expire its own Lease early and cause systematic duplicates
    protected override void AddParameters(List<NpgsqlParameter> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        parameters.Add(new NpgsqlParameter("lease", config.LeaseDuration));
    }
}
