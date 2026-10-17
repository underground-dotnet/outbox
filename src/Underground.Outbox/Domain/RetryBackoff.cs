using Underground.Outbox.Data;

namespace Underground.Outbox.Domain;

/// <summary>
/// The delay before a failed message is offered again: doubling so a partner system that is down is not
/// hammered, capped so recovery is picked up within a predictable time, and jittered so Groups that
/// failed against one shared dependency do not retry in lockstep.
/// </summary>
/// <param name="baseDelay">The delay after the first failed attempt.</param>
/// <param name="maxDelay">The ceiling the doubling stops at, before jitter.</param>
/// <param name="jitter">The proportion the capped delay is randomly varied by, either way.</param>
/// <typeparam name="TEntity">
/// The side whose configuration this follows. It appears in no member; what it selects is the registration.
/// </typeparam>
#pragma warning disable S2326 // Unused type parameters should be removed
internal sealed class RetryBackoff<TEntity>(TimeSpan baseDelay, TimeSpan maxDelay, double jitter) where TEntity : class, IMessage
#pragma warning restore S2326 // Unused type parameters should be removed
{
    /// <summary>
    /// The delay after a failed attempt, given how many had already failed before it. The first failure
    /// therefore waits <c>baseDelay</c>.
    /// </summary>
    /// <param name="retryCount">Failed attempts recorded before this one; never negative.</param>
    internal TimeSpan DelayFor(int retryCount)
    {
        // in double rather than ticks: the doubling overflows a TimeSpan within about sixty attempts,
        // whereas Math.Pow saturates at infinity, which Math.Min resolves to the cap
        var exponential = baseDelay.TotalMilliseconds * Math.Pow(2, retryCount);
        var capped = Math.Min(exponential, maxDelay.TotalMilliseconds);

        // after the cap, so a delay may exceed it: clamping back would pile retries up on the ceiling
        var spread = ((Random.Shared.NextDouble() * 2) - 1) * jitter;

        return TimeSpan.FromMilliseconds(capped * (1 + spread));
    }
}
