using Underground.Outbox.Data;
using Underground.Outbox.Exceptions;

namespace Underground.Outbox.Domain;

internal sealed class AddMessageToOutbox
{
#pragma warning disable CA1822, S2325 // Mark members as static
    public async Task ExecuteAsync(IOutboxDbContext context, IEnumerable<OutboxMessage> messages, CancellationToken cancellationToken)
#pragma warning restore CA1822, S2325 // Mark members as static
    {
        if (!HasActiveTransaction(context))
        {
            throw new NoActiveTransactionException();
        }

        await context.OutboxMessages.AddRangeAsync(messages, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    private static bool HasActiveTransaction(IDbContext context)
    {
        return context.Database.CurrentTransaction != null;
    }
}
