using System.Diagnostics;

using Microsoft.EntityFrameworkCore;

using Underground.Outbox.Data;
using Underground.Outbox.Exceptions;

namespace Underground.Outbox.Domain;

internal sealed class AddMessagesToInbox<TContext> where TContext : DbContext, IInboxDbContext
{
#pragma warning disable CA1822, S2325 // Mark members as static
    public async Task ExecuteAsync(TContext context, IEnumerable<InboxMessage> messages, CancellationToken cancellationToken)
#pragma warning restore CA1822, S2325 // Mark members as static
    {
        if (context.Database.CurrentTransaction is null)
        {
            throw new NoActiveTransactionException();
        }

        // materialised once: the trace context is stamped in a pass of its own, and AddRangeAsync enumerates again
        var toAdd = messages as IReadOnlyCollection<InboxMessage> ?? [.. messages];

        var traceParent = Activity.Current?.Id;
        foreach (var message in toAdd)
        {
            message.TraceParent = traceParent;
        }

        await context.InboxMessages.AddRangeAsync(toAdd, cancellationToken).ConfigureAwait(false);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
