using Microsoft.EntityFrameworkCore;

using Npgsql;

using Underground.Outbox.Configuration;
using Underground.Outbox.Data;

namespace Underground.Outbox.Domain;

/// <summary>
/// Claims a HeadMessage for the outbox by granting itself a Lease: the claim moves
/// <see cref="IMessage.VisibleAt"/> to the Lease expiry, so the message is out of sight for as long as
/// this worker has to finish and comes back on its own if the worker never does. Every later write is
/// guarded on the granted instant, which is what tells an expired worker the message is no longer its own.
/// </summary>
internal sealed class ClaimOutboxHeadMessage<TContext>(
    TContext dbContext,
    ServiceConfiguration<TContext, OutboxMessage> config
) : ClaimHeadMessage<TContext, OutboxMessage>(dbContext) where TContext : DbContext
{
    private readonly ServiceConfiguration<TContext, OutboxMessage> _config = config;

    protected override string Sql { get; } = StatementCache.GetOrAdd(config.QualifiedTable, "claim", Compose);

    private static string Compose(string qualifiedTable) => $"""
        {LockedHeadMessageCte(qualifiedTable)}
        UPDATE {qualifiedTable} m
        SET visible_at = clock_timestamp() + @lease
        FROM claimed c
        WHERE m.id = c.id
        RETURNING m.*
        """;

    // an interval rather than an instant, so a skewed application clock cannot expire a Lease early
    protected override void AddParameters(List<NpgsqlParameter> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        parameters.Add(new NpgsqlParameter("lease", _config.LeaseDuration));
    }
}
