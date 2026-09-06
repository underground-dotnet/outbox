namespace Underground.Outbox.Domain;

/// <summary>
/// What one turn of the outer loop found: whether a Head was claimed, and with it whether it is worth
/// looking again right away. It reports the claim and deliberately not the outcome - a failed message has
/// been pushed out of sight by its backoff, so reporting the claim cannot spin a worker.
/// </summary>
internal enum ClaimResult
{
    /// <summary>
    /// A Group offered its Head and this worker took it, so there may well be more waiting.
    /// </summary>
    HeadClaimed,

    /// <summary>
    /// No Group offered anything: nothing is unhandled, no candidate Head is visible, or other workers
    /// hold the ones that are. A worker that sees this waits.
    /// </summary>
    NothingOffered,
}
