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
/// goes through the <see cref="Claim{TEntity}"/>, guarded on the Lease, which stops a worker that overran from
/// overwriting a newer Claim.
/// </remarks>
internal sealed class OutboxProcessor(
    IOutboxDbContext dbContext,
    ClaimHeadMessage<OutboxMessage> claimHeadMessage,
    MessagePipeline<OutboxMessage> pipeline
) : IProcessor<OutboxMessage>
{
    public async Task<ClaimResult> TryProcessHeadMessageAsync(CancellationToken cancellationToken)
    {
        // .net aspire compatability
        // Only the claim is wrapped: the dispatch and the outcome write open no transaction of their own,
        // and replaying them from here would replay the Handler with them. What happens to a failure in
        // them is unchanged - the Lease expires and the message is offered again. See ADR 0006.
        // The Lease only exists once that transaction commits; until then the row is merely locked.
        var claim = await dbContext
            .ExecuteInTransactionAsync<Claim<OutboxMessage>?>(claimHeadMessage.ExecuteAsync, cancellationToken)
            .ConfigureAwait(false);

        if (claim is null)
        {
            return ClaimResult.NothingOffered;
        }

        dbContext.ChangeTracker.Clear();

        await pipeline.ExecuteAsync(claim, cancellationToken).ConfigureAwait(false);

        dbContext.ChangeTracker.Clear();

        return ClaimResult.HeadMessageClaimed;
    }
}
