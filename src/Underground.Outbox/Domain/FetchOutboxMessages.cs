using Underground.Outbox.Data;

using System.Data.Common;

using Microsoft.Extensions.Logging;

namespace Underground.Outbox.Domain;

internal sealed class FetchOutboxMessages(IDbContext dbContext, ILogger<FetchOutboxMessages> logger) : FetchMessages<OutboxMessage>(dbContext, logger)
{
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
