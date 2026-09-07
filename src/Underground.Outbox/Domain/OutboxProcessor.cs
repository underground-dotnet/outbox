using Microsoft.Extensions.DependencyInjection;

using Underground.Outbox.Data;
using Underground.Outbox.Domain.Middleware;

namespace Underground.Outbox.Domain;

/// <summary>
/// The outbox outer loop, in three transactions: the claim commits a Lease, the Handler runs with nothing
/// open, and the outcome is written on its own. It exists to avoid holding a database transaction across
/// a call whose latency we do not control. See ADR 0001.
/// </summary>
/// <remarks>
/// The cost is at-least-once delivery, so outbox Handlers must be idempotent. Every write after the claim
/// is guarded on the granted Lease, which stops a worker that overran from overwriting a newer claim.
/// </remarks>
internal sealed class OutboxProcessor(
    IDbContext dbContext,
    ClaimHeadMessage<OutboxMessage> claimHeadMessage,
    MessagePipeline<OutboxMessage> pipeline
) : IProcessor<OutboxMessage>
{
    public async Task<ClaimResult> TryProcessHeadMessageAsync(IServiceScope scope, CancellationToken cancellationToken)
    {
        // .net aspire compatability
        // Only the claim is wrapped: the dispatch and the outcome write open no transaction of their own,
        // and replaying them from here would replay the Handler with them. What happens to a failure in
        // them is unchanged - the Lease expires and the message is offered again. See ADR 0006.
        // The Lease only exists once that transaction commits; until then the row is merely locked.
        var message = await dbContext
            .ExecuteInTransactionAsync<OutboxMessage?>(claimHeadMessage.ExecuteAsync, cancellationToken)
            .ConfigureAwait(false);

        if (message is null)
        {
            return ClaimResult.NothingOffered;
        }

        dbContext.ChangeTracker.Clear();

        await pipeline.ExecuteAsync(message, scope, cancellationToken).ConfigureAwait(false);

        dbContext.ChangeTracker.Clear();

        return ClaimResult.HeadMessageClaimed;
    }
}
