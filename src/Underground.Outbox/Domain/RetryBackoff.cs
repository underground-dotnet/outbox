namespace Underground.Outbox.Domain;

/// <summary>
/// The delay before a failed message is offered again: doubling with every attempt so a partner
/// system that is down is not hammered, capped so a message that recovers after a long outage is
/// still picked up within a predictable time, and jittered so that Groups which all failed against
/// one shared dependency do not retry in lockstep.
/// </summary>
/// <param name="baseDelay">The delay after the first failed attempt.</param>
/// <param name="maxDelay">The ceiling the doubling stops at, before jitter.</param>
/// <param name="jitter">The proportion the capped delay is randomly varied by, either way.</param>
internal sealed class RetryBackoff(TimeSpan baseDelay, TimeSpan maxDelay, double jitter)
{
    /// <summary>
    /// The delay to apply after an attempt that failed, given how many attempts had already failed
    /// before it. The first failure of a message therefore waits <c>baseDelay</c>.
    /// </summary>
    /// <param name="retryCount">Failed attempts recorded before this one; never negative.</param>
    internal TimeSpan DelayFor(int retryCount)
    {
        // computed in double rather than in ticks: the doubling overflows a TimeSpan within about sixty
        // attempts, and a message that fails forever reaches that. Math.Pow saturates at infinity instead,
        // which Math.Min then resolves to the cap.
        var exponential = baseDelay.TotalMilliseconds * Math.Pow(2, retryCount);
        var capped = Math.Min(exponential, maxDelay.TotalMilliseconds);

        // Jitter is applied after the cap, so a delay may exceed the cap by the jitter proportion. Spreading
        // retries out is the point of it, and clamping it back to the cap would pile them up on the ceiling.
        var spread = ((Random.Shared.NextDouble() * 2) - 1) * jitter;

        return TimeSpan.FromMilliseconds(capped * (1 + spread));
    }
}
