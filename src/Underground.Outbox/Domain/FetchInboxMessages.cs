using Underground.Outbox.Data;

using System.Data.Common;

using Microsoft.Extensions.Logging;

namespace Underground.Outbox.Domain;

internal sealed class FetchInboxMessages(IDbContext dbContext, ILogger<FetchMessages<InboxMessage>> logger) : FetchMessages<InboxMessage>(dbContext, logger)
{
    protected override InboxMessage BuildEntityFromReader(DbDataReader reader) => new(
        id: reader.GetInt64(0),
        eventId: reader.GetGuid(1),
        createdAt: reader.GetDateTime(2),
        type: reader.GetString(3),
        partitionKey: reader.GetString(4),
        data: reader.GetString(5),
        retryCount: reader.GetInt32(6),
        processedAt: reader.IsDBNull(7) ? null : reader.GetDateTime(7)
    );
}
