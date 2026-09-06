namespace Underground.Outbox.Domain;

/// <summary>
/// What one turn of the outer loop found: whether a Head was claimed, and with it whether it is worth
/// looking again right away.
/// </summary>
/// <remarks>
/// This reports the claim and deliberately not the outcome. A message that was claimed and then failed
/// still counts as claimed - it has been pushed out of sight by its backoff, so the next claim looks past
/// it at some other Group's Head - which is what stops a failing message from spinning a worker.
/// </remarks>
internal enum ClaimResult
{
    /// <summary>
    /// A Group offered its Head and this worker took it. Something was done to a message, whatever became
    /// of it, so there may well be more waiting.
    /// </summary>
    HeadClaimed,

    /// <summary>
    /// No Group offered anything - because nothing is unhandled, because every candidate Head is not yet
    /// visible, or because other workers hold the ones that are. A worker that sees this waits.
    /// </summary>
    NothingOffered,
}
