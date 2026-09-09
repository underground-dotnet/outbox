using System.Diagnostics;

using Microsoft.EntityFrameworkCore;

using Underground.Outbox.Data;
using Underground.Outbox.Exceptions;

namespace Underground.Outbox.Domain;

internal sealed class AddMessagesToOutbox<TContext> where TContext : DbContext, IOutboxDbContext
{
#pragma warning disable CA1822, S2325 // Mark members as static
    public async Task ExecuteAsync(TContext context, IEnumerable<OutboxMessage> messages, CancellationToken cancellationToken)
#pragma warning restore CA1822, S2325 // Mark members as static
    {
        if (context.Database.CurrentTransaction is null)
        {
            throw new NoActiveTransactionException();
        }

        // materialised once: the trace context is stamped in a pass of its own, and AddRangeAsync enumerates again
        var toAdd = messages as IReadOnlyCollection<OutboxMessage> ?? [.. messages];

        var traceParent = Activity.Current?.Id;
        foreach (var message in toAdd)
        {
            message.TraceParent = traceParent;
        }

        await context.OutboxMessages.AddRangeAsync(toAdd, cancellationToken).ConfigureAwait(false);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
