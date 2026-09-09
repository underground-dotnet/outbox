using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Underground.Outbox.Data;
using Underground.Outbox.Domain.Middleware;

namespace Underground.Outbox.Domain;

/// <summary>
/// The inbox outer loop: one transaction spans the claim, the Handler and the write that records the
/// outcome, so an inbox message is applied exactly once. The row lock the claim takes is held for the
/// length of that transaction and dies with the connection, so nothing has to expire.
/// </summary>
/// <remarks>
/// The cost is that this transaction is a writer from the claim onwards, so it pins
/// <c>pg_snapshot_xmin</c> - the watermark HeadMessage discovery gates on - and stalls discovery for
/// every registered inbox and outbox in the process, not just this one. Inbox Handlers must therefore stay
/// short. See ADR 0002 and ADR 0008.
/// </remarks>
internal sealed class InboxProcessor<TContext>(
    TContext dbContext,
    ClaimHeadMessage<TContext, InboxMessage> claimHeadMessage,
    MessagePipeline<TContext, InboxMessage> pipeline
) : IProcessor<TContext, InboxMessage> where TContext : DbContext, IInboxDbContext
{
    public async Task<ClaimResult> TryProcessHeadMessageAsync(IServiceScope scope, CancellationToken cancellationToken)
    {
        // .net aspire compatability
        // One transaction spans the whole attempt, so the attempt is also what a transient failure
        // replays. See ADR 0006.
        return await dbContext.ExecuteInTransactionAsync(async ct =>
        {
            // a replayed attempt inherits what the failed one tracked, whose transaction is gone
            dbContext.ChangeTracker.Clear();

            var message = await claimHeadMessage.ExecuteAsync(ct).ConfigureAwait(false);
            if (message is null)
            {
                return ClaimResult.NothingOffered;
            }

            await pipeline.ExecuteAsync(message, scope, ct).ConfigureAwait(false);

            return ClaimResult.HeadMessageClaimed;
        }, cancellationToken).ConfigureAwait(false);
    }
}
