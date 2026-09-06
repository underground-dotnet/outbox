using Underground.Outbox.Data;

namespace Underground.Outbox.Domain;

/// <summary>
/// Claims a HeadMessage for the inbox by holding the row lock the discovery CTE took. The claim, the Handler and
/// the outcome write all run in one transaction, so nothing has to be granted and nothing can expire.
/// </summary>
internal sealed class ClaimInboxHeadMessage(IDbContext dbContext) : ClaimHeadMessage<InboxMessage>(dbContext)
{
    // the CTE's lock is held until the transaction ends, so this only reads the row back out
    private static readonly string ClaimSql = $"""
        {LockedHeadMessageCte()}
        SELECT m.*
        FROM claimed c
        JOIN {InboxMessage.TableName} m ON m.id = c.id
        """;

    protected override string Sql => ClaimSql;
}
