using Underground.Outbox.Data;
using Underground.Outbox.Exceptions;

namespace Underground.Outbox.Domain;

internal sealed class AddMessageToOutbox
{
#pragma warning disable CA1822, S2325 // Mark members as static
    public async Task ExecuteAsync(IOutboxDbContext context, OutboxMessage message, CancellationToken cancellationToken = default)
#pragma warning restore CA1822, S2325 // Mark members as static
    {
        if (!HasActiveTransaction(context))
        {
            throw new NoActiveTransactionException();
        }

        await context.OutboxMessages.AddAsync(message, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    private static bool HasActiveTransaction(IOutboxDbContext context)
    {
        return context.Database.CurrentTransaction != null;
    }
}
