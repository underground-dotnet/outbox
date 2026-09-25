using Underground.Outbox.Data;

namespace Underground.Outbox.Domain;

/// <summary>
/// Claims a HeadMessage for the inbox by holding the row lock the discovery CTE took. The claim, the Handler and
/// the outcome write all run in one transaction, so nothing has to be granted and nothing can expire.
/// </summary>
internal sealed class ClaimInboxHeadMessage(IInboxDbContext dbContext) : ClaimHeadMessage<InboxMessage>(dbContext)
{
    // the CTE's lock is held until the transaction ends, so this only hands out the row version it locked
    private static readonly ClaimStatements Claim = BuildStatements("SELECT * FROM claimed");

    protected override ClaimStatements Statements => Claim;
}
