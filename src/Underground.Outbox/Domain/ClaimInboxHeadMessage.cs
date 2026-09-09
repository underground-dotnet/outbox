using Microsoft.EntityFrameworkCore;

using Underground.Outbox.Configuration;
using Underground.Outbox.Data;

namespace Underground.Outbox.Domain;

/// <summary>
/// Claims a HeadMessage for the inbox by holding the row lock the discovery CTE took. The claim, the Handler and
/// the outcome write all run in one transaction, so nothing has to be granted and nothing can expire.
/// </summary>
internal sealed class ClaimInboxHeadMessage<TContext>(
    TContext dbContext,
    ServiceConfiguration<TContext, InboxMessage> config
) : ClaimHeadMessage<TContext, InboxMessage>(dbContext) where TContext : DbContext
{
    protected override string Sql { get; } = StatementCache.GetOrAdd(config.QualifiedTable, "claim", Compose);

    // the CTE's lock is held until the transaction ends, so this only reads the row back out
    private static string Compose(string qualifiedTable) => $"""
        {LockedHeadMessageCte(qualifiedTable)}
        SELECT m.*
        FROM claimed c
        JOIN {qualifiedTable} m ON m.id = c.id
        """;
}
