using Microsoft.Extensions.Logging;

using Npgsql;

using Underground.Outbox.Data;
using Underground.Outbox.Domain.Inboxes;

namespace Underground.Outbox.Domain;

/// <summary>
/// Claims a HeadMessage for one Inbox by holding the row lock the discovery CTE took. The claim, the Handler and
/// the outcome write all run in one transaction, so nothing has to be granted and nothing can expire.
/// </summary>
internal sealed class ClaimInboxHeadMessage(
    MessageDbContext<InboxMessage> messageDbContext,
    InboxScope scope,
    ILoggerFactory loggerFactory
) : ClaimHeadMessage<InboxMessage>(messageDbContext.Context, loggerFactory)
{
    // the CTE's lock is held until the transaction ends, so this only hands out the row version it locked
    private static readonly string ClaimStatement = BuildStatement("SELECT * FROM claimed", scopeColumn: "inbox");

    protected override string Statement => ClaimStatement;

    protected override void AddParameters(List<NpgsqlParameter> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        parameters.Add(new NpgsqlParameter("inbox", scope.Inbox.Name));
    }
}
