using System.Diagnostics;

using Underground.Outbox.Data;
using Underground.Outbox.Exceptions;

namespace Underground.Outbox.Domain;

internal sealed class AddMessagesToInbox
{
#pragma warning disable CA1822, S2325 // Mark members as static
    public async Task ExecuteAsync(IInboxDbContext context, IEnumerable<InboxMessage> messages, CancellationToken cancellationToken)
#pragma warning restore CA1822, S2325 // Mark members as static
    {
        if (!HasActiveTransaction(context))
        {
            throw new NoActiveTransactionException(InboxMessage.TableName);
        }

        Stage(context, messages);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

#pragma warning disable CA1822, S2325 // Mark members as static
    public void Stage(IInboxDbContext context, IEnumerable<InboxMessage> messages)
#pragma warning restore CA1822, S2325 // Mark members as static
    {
        // materialised once: the trace context is stamped in a pass of its own, and AddRange enumerates again
        var toAdd = messages as IReadOnlyCollection<InboxMessage> ?? [.. messages];

        var traceParent = Activity.Current?.Id;
        foreach (var message in toAdd)
        {
            message.TraceParent = traceParent;
        }

        context.InboxMessages.AddRange(toAdd);
    }

    private static bool HasActiveTransaction(IDbContext context)
    {
        return context.Database.CurrentTransaction != null;
    }
}
