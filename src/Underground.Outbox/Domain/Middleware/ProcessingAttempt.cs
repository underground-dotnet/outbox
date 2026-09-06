namespace Underground.Outbox.Domain.Middleware;

/// <summary>
/// What became of one run of the MiddlewarePipeline against one HeadMessage, passed back out through the middleware. A summary of
/// what has already been recorded, not the means of recording it.
/// </summary>
/// <remarks>
/// The constructor is private because two of the four field combinations are nonsense; the factories
/// below are the only shapes that exist.
/// </remarks>
internal sealed record ProcessingAttempt
{
    private ProcessingAttempt(ProcessingStatus status, Exception? failure)
    {
        Status = status;
        Failure = failure;
    }

    /// <summary>
    /// What became of the message.
    /// </summary>
    public ProcessingStatus Status { get; }

    /// <summary>
    /// What the Handler threw, if it threw. Null on <see cref="ProcessingStatus.Succeeded"/>, and on a
    /// <see cref="ProcessingStatus.LeaseLost"/> that happened at completion rather than after a failure.
    /// </summary>
    public Exception? Failure { get; }

    /// <summary>
    /// The Handler returned and the message was marked completed.
    /// </summary>
    public static ProcessingAttempt Succeeded { get; } = new(ProcessingStatus.Succeeded, failure: null);

    /// <summary>
    /// The Handler threw and the attempt was recorded against the message.
    /// </summary>
    public static ProcessingAttempt Failed(Exception failure) => new(ProcessingStatus.FailureRecorded, failure);

    /// <summary>
    /// This worker no longer holds the message, so nothing it did was recorded.
    /// </summary>
    /// <param name="failure">
    /// What the Handler threw, if it threw. No failure means the Handler succeeded and the completion write
    /// came too late, so the effect has been carried out twice.
    /// </param>
    public static ProcessingAttempt LeaseLost(Exception? failure) => new(ProcessingStatus.LeaseLost, failure);
}
