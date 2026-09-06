namespace Underground.Outbox.Domain.Chain;

/// <summary>
/// What became of one run of the Chain against one Head. Every completed run ends in exactly one of
/// these, settled by whichever Stage recorded it.
/// </summary>
/// <remarks>
/// A run that does not complete - the application shutting down mid-attempt - produces none of these: the
/// <see cref="OperationCanceledException"/> travels out past every Stage and nothing was recorded.
/// </remarks>
internal enum AttemptStatus
{
    /// <summary>
    /// The Handler returned and the message was marked handled.
    /// </summary>
    Handled,

    /// <summary>
    /// The Handler threw and the attempt was recorded: the retry count is up and the message is out of
    /// sight for its backoff delay.
    /// </summary>
    FailureRecorded,

    /// <summary>
    /// The guarded write matched no row, so this worker no longer holds the message and nothing it did was
    /// recorded. See <see cref="Attempt.Failure"/> for which of the two ways that happened.
    /// </summary>
    LeaseLost,
}
