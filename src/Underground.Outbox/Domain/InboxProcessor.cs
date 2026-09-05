using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Underground.Outbox.Data;
using Underground.Outbox.Domain.Chain;

namespace Underground.Outbox.Domain;

/// <summary>
/// The inbox outer loop: one transaction spans the claim, the Handler and the write that records the
/// outcome, so an inbox message is applied exactly once. The row lock the claim takes is held for the
/// length of that transaction and dies with the connection, so nothing has to expire.
/// </summary>
internal sealed class InboxProcessor(
    IDbContext dbContext,
    ClaimHead<InboxMessage> claimHead,
    MessageChain<InboxMessage> chain
) : IProcessor<InboxMessage>
{
    public async Task<bool> ProcessHeadAsync(IServiceScope scope, CancellationToken cancellationToken)
    {
        var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            var message = await claimHead.ExecuteAsync(cancellationToken).ConfigureAwait(false);
            if (message is null)
            {
                return false;
            }

            await chain.ExecuteAsync(message, scope, cancellationToken).ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            // remove tracked entities to avoid memory leaks
            dbContext.ChangeTracker.Clear();

            // a failed message has been pushed out of sight by the backoff, so reporting the claim rather
            // than the outcome cannot spin: the next claim looks past it, at some other Group's Head
            return true;
        }
    }
}
