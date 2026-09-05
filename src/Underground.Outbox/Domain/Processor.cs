using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Underground.Outbox.Data;
using Underground.Outbox.Domain.Chain;

namespace Underground.Outbox.Domain;

internal sealed class Processor<TEntity>(
    IDbContext dbContext,
    ClaimHead<TEntity> claimHead,
    MessageChain<TEntity> chain
) where TEntity : class, IMessage
{
    /// <summary>
    /// Claims and handles one Head - the oldest settled unhandled message of whichever Group offers the
    /// oldest one - using the given scope and the DbContext of this instance. A Group offers only its Head,
    /// and it offers nothing at all while that Head is not yet visible, so a message in backoff or scheduled
    /// for later holds back everything behind it in the same Group without holding back any other Group.
    /// </summary>
    /// <remarks>
    /// This is the outer loop: the transaction boundary, the claim and the write that records the outcome.
    /// Everything done to the message in between is <see cref="MessageChain{TEntity}"/>, which the inbox and
    /// the outbox share.
    /// </remarks>
    /// <returns>
    /// Whether a message was claimed, and with it whether it is worth calling again right away. A message
    /// that was claimed and then failed still counts as claimed; it is <c>false</c> only when no Group
    /// offered anything.
    /// </returns>
    internal async Task<bool> ProcessHeadAsync(IServiceScope scope, CancellationToken cancellationToken)
    {
        var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            var message = await claimHead.ExecuteAsync(cancellationToken).ConfigureAwait(false);
            if (message is null)
            {
                return false;
            }

            var messageId = message.Id;

            var handled = await chain.ExecuteAsync(message, scope, cancellationToken).ConfigureAwait(false);

            if (handled)
            {
                await dbContext.Set<TEntity>()
                    .Where(m => m.Id == messageId)
                    .ExecuteUpdateAsync(update => update.SetProperty(m => m.ProcessedAt, DateTime.UtcNow), cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            // remove tracked entities to avoid memory leaks
            dbContext.ChangeTracker.Clear();

            // a failed message has been pushed out of sight by the backoff, so reporting the claim rather
            // than the outcome cannot spin: the next claim looks past it, at some other Group's Head
            return true;
        }
    }
}
