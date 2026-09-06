namespace Underground.Outbox.Domain.Middleware;

/// <summary>
/// What became of one run of the MiddlewarePipeline against one HeadMessage. Every completed run ends in exactly one of these.
/// A run cut short by shutdown produces none: the <see cref="OperationCanceledException"/> travels out
/// past every middleware and nothing was recorded.
/// </summary>
internal enum ProcessingStatus
{
    /// <summary>
    /// The Handler returned and the message was marked completed.
    /// </summary>
    Succeeded,

    /// <summary>
    /// The Handler threw: the retry count is up and the message is out of sight for its backoff delay.
    /// </summary>
    FailureRecorded,

    /// <summary>
    /// The guarded write matched no row, so this worker no longer holds the message and nothing it did was
    /// recorded. <see cref="ProcessingAttempt.Failure"/> says which of the two ways that happened.
    /// </summary>
    LeaseLost,
}
