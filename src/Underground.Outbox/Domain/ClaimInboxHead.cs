using Underground.Outbox.Data;

using System.Data.Common;

using Microsoft.Extensions.Logging;

namespace Underground.Outbox.Domain;

/// <summary>
/// Claims a Head for the inbox by holding the row lock the discovery CTE took. The claim, the Handler and
/// the write that records the outcome all run in the one transaction, so nothing has to be granted and
/// nothing can expire.
/// </summary>
internal sealed class ClaimInboxHead(IDbContext dbContext, ILogger<ClaimInboxHead> logger) : ClaimHead<InboxMessage>(dbContext, logger)
{
    // the lock the CTE took is held until the transaction ends, so the outer statement only has to read
    // the row back out
    protected override string BuildSql(MessageTable table) => $"""
        {LockedHeadCte(table)}
        SELECT {Projection(table)}
        FROM claimed c
        JOIN {table.Name} m ON m.{table.Id} = c.{table.Id}
        """;

    protected override InboxMessage BuildEntityFromReader(DbDataReader reader) => new(
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
