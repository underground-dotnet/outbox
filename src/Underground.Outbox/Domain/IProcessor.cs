using Microsoft.Extensions.DependencyInjection;

using Underground.Outbox.Data;

namespace Underground.Outbox.Domain;

/// <summary>
/// The outer loop around one message: the transaction boundary, the claim, and the write that records
/// the outcome. Everything done to the message in between is
/// <see cref="Chain.MessageChain{TEntity}"/>, which the inbox and the outbox share.
/// </summary>
/// <remarks>
/// The two sides have their own implementation because this is precisely where they differ - the inbox
/// spans all three in one transaction and is exactly-once, the outbox commits a Lease and dispatches
/// with nothing open and is at-least-once - and expressing that difference as a flag would mean every
/// line branching on it. See ADR 0001.
/// </remarks>
/// <typeparam name="TEntity">
/// The side this loop serves. It appears in no signature here: what it selects is the implementation,
/// which is the whole of what a worker needs to know.
/// </typeparam>
#pragma warning disable S2326 // Unused type parameters should be removed
internal interface IProcessor<TEntity> where TEntity : class, IMessage
#pragma warning restore S2326 // Unused type parameters should be removed
{
    /// <summary>
    /// Claims and handles one Head - the oldest settled unhandled message of whichever Group offers the
    /// oldest one - using the given scope and the DbContext of this instance. A Group offers only its Head,
    /// and it offers nothing at all while that Head is not yet visible, so a message in backoff or scheduled
    /// for later holds back everything behind it in the same Group without holding back any other Group.
    /// </summary>
    /// <returns>
    /// Whether a message was claimed, and with it whether it is worth calling again right away. A message
    /// that was claimed and then failed still counts as claimed; it is <c>false</c> only when no Group
    /// offered anything.
    /// </returns>
    Task<bool> ProcessHeadAsync(IServiceScope scope, CancellationToken cancellationToken);
}
