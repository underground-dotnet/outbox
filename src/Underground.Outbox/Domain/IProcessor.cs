using Microsoft.Extensions.DependencyInjection;

using Underground.Outbox.Data;

namespace Underground.Outbox.Domain;

/// <summary>
/// The outer loop around one message: the transaction boundary, the claim, and the write that records the
/// outcome. Everything in between is <see cref="Chain.MessageChain{TEntity}"/>, which both sides share.
/// </summary>
/// <remarks>
/// One implementation per side, because this is where they differ: the inbox spans all three in one
/// transaction and is exactly-once, the outbox commits a Lease and dispatches with nothing open and is
/// at-least-once. See ADR 0001.
/// </remarks>
/// <typeparam name="TEntity">
/// The side this loop serves. It appears in no signature here; what it selects is the implementation.
/// </typeparam>
#pragma warning disable S2326 // Unused type parameters should be removed
internal interface IProcessor<TEntity> where TEntity : class, IMessage
#pragma warning restore S2326 // Unused type parameters should be removed
{
    /// <summary>
    /// Claims and handles one Head - the oldest settled unhandled message of whichever Group offers the
    /// oldest one. A Group offers only its Head, and nothing at all while that Head is not yet visible, so
    /// a message in backoff holds back its own Group and no other.
    /// </summary>
    /// <returns>
    /// Whether a message was claimed. A message that was claimed and then failed still counts as claimed.
    /// </returns>
    Task<ClaimResult> ProcessHeadAsync(IServiceScope scope, CancellationToken cancellationToken);
}
