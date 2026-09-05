using Underground.Outbox.Data;

namespace Underground.Outbox.Domain;

/// <summary>
/// Claims a Head for the inbox by holding the row lock the discovery CTE took. The claim, the Handler and
/// the write that records the outcome all run in the one transaction, so nothing has to be granted and
/// nothing can expire.
/// </summary>
internal sealed class ClaimInboxHead(IDbContext dbContext) : ClaimHead<InboxMessage>(dbContext)
{
    // the lock the CTE took is held until the transaction ends, so the outer statement only has to read
    // the row back out
    private static readonly string ClaimSql = $"""
        {LockedHeadCte()}
        SELECT m.*
        FROM claimed c
        JOIN {InboxMessage.TableName} m ON m.id = c.id
        """;

    protected override string Sql => ClaimSql;
}
