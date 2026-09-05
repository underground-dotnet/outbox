using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Underground.Outbox.Data;
using Underground.Outbox.Domain.Chain;

namespace Underground.Outbox.Domain;

/// <summary>
/// The outbox outer loop, in three transactions: the claim commits a Lease, the Handler runs with
/// nothing open, and the outcome is written on its own. An outbox Handler talks to a system whose
/// latency we do not control, and holding a database transaction across that is what this exists to
/// avoid. See ADR 0001.
/// </summary>
/// <remarks>
/// The cost is at-least-once delivery. A worker that dies between the external effect and the
/// completion write no longer blocks its Group - the Lease expires and the message is offered again -
/// but the effect happens twice, so outbox Handlers must be idempotent. Every write after the claim is
/// guarded on the granted Lease, which is what stops a worker that overran from overwriting a newer
/// worker's claim and fanning the message out a third time.
/// </remarks>
internal sealed class OutboxProcessor(
    IDbContext dbContext,
    ClaimHead<OutboxMessage> claimHead,
    MarkHandled<OutboxMessage> markHandled,
    MessageChain<OutboxMessage> chain
) : IProcessor<OutboxMessage>
{
    public async Task<bool> ProcessHeadAsync(IServiceScope scope, CancellationToken cancellationToken)
    {
        OutboxMessage? message;

        var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            message = await claimHead.ExecuteAsync(cancellationToken).ConfigureAwait(false);
            if (message is null)
            {
                return false;
            }

            // the Lease only exists once this commits: until then the row is merely locked, and a worker
            // that died here would have left the Group blocked rather than merely leased
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        // remove tracked entities to avoid memory leaks
        dbContext.ChangeTracker.Clear();

        var handled = await chain.ExecuteAsync(message, scope, cancellationToken).ConfigureAwait(false);

        if (handled)
        {
            // a lost Lease here is a warning rather than a failure: the effect really did happen, and the
            // message is simply no longer ours to mark
            await markHandled.ExecuteAsync(message, cancellationToken).ConfigureAwait(false);
        }

        dbContext.ChangeTracker.Clear();

        // a failed message has been pushed out of sight by the backoff, so reporting the claim rather
        // than the outcome cannot spin: the next claim looks past it, at some other Group's Head
        return true;
    }
}
