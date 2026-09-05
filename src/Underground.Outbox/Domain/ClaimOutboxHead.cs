using Underground.Outbox.Configuration;
using Underground.Outbox.Data;

using System.Data.Common;

using Microsoft.Extensions.Logging;

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
    ServiceConfiguration<OutboxMessage> config,
    ILogger<ClaimOutboxHead> logger
) : ClaimHead<OutboxMessage>(dbContext, logger)
{
    protected override string BuildSql(MessageTable table) => $"""
        {LockedHeadCte(table)}
        UPDATE {table.Name} m
        SET {table.VisibleAt} = clock_timestamp() + @lease
        FROM claimed c
        WHERE m.{table.Id} = c.{table.Id}
        RETURNING {Projection(table)}
        """;

    // an interval rather than an instant, so that the expiry is computed by the database: an instance with
    // a skewed clock cannot expire its own Lease early and cause systematic duplicates
    protected override void AddParameters(DbCommand command)
    {
        var lease = command.CreateParameter();
        lease.ParameterName = "lease";
        lease.Value = config.LeaseDuration;

        command.Parameters.Add(lease);
    }

    protected override OutboxMessage BuildEntityFromReader(DbDataReader reader) => new(
        id: reader.GetInt64(0),
        eventId: reader.GetGuid(1),
        transactionId: reader.GetFieldValue<ulong>(2),
        createdAt: reader.GetDateTime(3),
        type: reader.GetString(4),
        groupKey: reader.GetString(5),
        data: reader.GetString(6),
        retryCount: reader.GetInt32(7),
        visibleAt: reader.GetDateTime(8),
        processedAt: reader.IsDBNull(9) ? null : reader.GetDateTime(9)
    );
}
