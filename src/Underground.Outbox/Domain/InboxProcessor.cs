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
/// both sides and every Group while the Handler runs. Inbox Handlers must therefore stay short. See
/// ADR 0002.
/// </remarks>
internal sealed class InboxProcessor(
    IDbContext dbContext,
    ClaimHeadMessage<InboxMessage> claimHeadMessage,
    MessagePipeline<InboxMessage> pipeline
) : IProcessor<InboxMessage>
{
    public async Task<ClaimResult> TryProcessHeadMessageAsync(IServiceScope scope, CancellationToken cancellationToken)
    {
        var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            var message = await claimHeadMessage.ExecuteAsync(cancellationToken).ConfigureAwait(false);
            if (message is null)
            {
                return ClaimResult.NothingOffered;
            }

            await pipeline.ExecuteAsync(message, scope, cancellationToken).ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            dbContext.ChangeTracker.Clear();

            return ClaimResult.HeadMessageClaimed;
        }
    }
}
