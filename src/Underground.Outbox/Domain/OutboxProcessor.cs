using Microsoft.EntityFrameworkCore;
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
        OutboxMessage? message;

        var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            message = await claimHeadMessage.ExecuteAsync(cancellationToken).ConfigureAwait(false);
            if (message is null)
            {
                return ClaimResult.NothingOffered;
            }

            // the Lease only exists once this commits; until then the row is merely locked
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        dbContext.ChangeTracker.Clear();

        await pipeline.ExecuteAsync(message, scope, cancellationToken).ConfigureAwait(false);

        dbContext.ChangeTracker.Clear();

        return ClaimResult.HeadMessageClaimed;
    }
}
