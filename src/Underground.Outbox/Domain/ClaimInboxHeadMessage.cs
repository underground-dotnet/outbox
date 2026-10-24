using Microsoft.Extensions.Logging;

using Underground.Outbox.Data;

namespace Underground.Outbox.Domain;

/// <summary>
/// Claims a HeadMessage for the inbox by holding the row lock the discovery CTE took. The claim, the Handler and
/// the outcome write all run in one transaction, so nothing has to be granted and nothing can expire.
/// </summary>
internal sealed class ClaimInboxHeadMessage(
    IInboxDbContext dbContext,
    ILoggerFactory loggerFactory
) : ClaimHeadMessage<InboxMessage>(dbContext, loggerFactory)
{
    // The CTE's lock is held until the transaction ends, so this only hands out the row version it locked.
    // A Group belongs to one Recipient, so one Recipient's stalled message never holds back another's (ADR 0012).
    private static readonly string ClaimStatement = BuildStatement("SELECT * FROM claimed", "recipient", "group_key");

    protected override string Statement => ClaimStatement;
}
