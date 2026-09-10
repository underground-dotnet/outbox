using System.Diagnostics;

using Underground.Outbox.Data;
using Underground.Outbox.Exceptions;

namespace Underground.Outbox.Domain;

internal sealed class AddMessagesToOutbox
{
#pragma warning disable CA1822, S2325 // Mark members as static
    public async Task ExecuteAsync(IOutboxDbContext context, IEnumerable<OutboxMessage> messages, CancellationToken cancellationToken)
#pragma warning restore CA1822, S2325 // Mark members as static
    {
        if (!HasActiveTransaction(context))
        {
            throw new NoActiveTransactionException(OutboxMessage.TableName);
        }

        Stage(context, messages);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

#pragma warning disable CA1822, S2325 // Mark members as static
    public void Stage(IOutboxDbContext context, IEnumerable<OutboxMessage> messages)
#pragma warning restore CA1822, S2325 // Mark members as static
    {
        // materialised once: the trace context is stamped in a pass of its own, and AddRange enumerates again
        var toAdd = messages as IReadOnlyCollection<OutboxMessage> ?? [.. messages];

        var traceParent = Activity.Current?.Id;
        foreach (var message in toAdd)
        {
            message.TraceParent = traceParent;
        }

        context.OutboxMessages.AddRange(toAdd);
    }

    private static bool HasActiveTransaction(IDbContext context)
    {
        return context.Database.CurrentTransaction != null;
    }
}
