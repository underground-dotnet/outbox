namespace Underground.Outbox.Domain.Chain;

/// <summary>
/// What became of one run of the Chain against one Head, passed back out through the Stages. It is a
/// summary of what has already been recorded rather than the means of recording it: a Handler that fails
/// still fails by throwing, and by the time any Stage reads an Attempt the exception has been dealt with.
/// </summary>
/// <remarks>
/// The constructor is private because two of the four field combinations are nonsense - a
/// <see cref="AttemptStatus.Handled"/> attempt with a failure, a <see cref="AttemptStatus.FailureRecorded"/>
/// one without. The factories below are the only shapes that exist.
/// </remarks>
internal sealed record Attempt
{
    private Attempt(AttemptStatus status, Exception? failure)
    {
        Status = status;
        Failure = failure;
    }

    /// <summary>
    /// What became of the message.
    /// </summary>
    public AttemptStatus Status { get; }

    /// <summary>
    /// What the Handler threw, where it threw at all. Null on <see cref="AttemptStatus.Handled"/>, and on a
    /// <see cref="AttemptStatus.LeaseLost"/> that happened at completion rather than after a failure.
    /// </summary>
    public Exception? Failure { get; }

    /// <summary>
    /// The Handler returned and the message was marked handled.
    /// </summary>
    public static Attempt Handled { get; } = new(AttemptStatus.Handled, failure: null);

    /// <summary>
    /// The Handler threw and the attempt was recorded against the message.
    /// </summary>
    public static Attempt Failed(Exception failure) => new(AttemptStatus.FailureRecorded, failure);

    /// <summary>
    /// This worker no longer holds the message, so nothing it did was recorded.
    /// </summary>
    /// <param name="failure">
    /// What the Handler threw, if it threw. A failure here means an attempt was discarded; no failure means
    /// the Handler succeeded and the completion write was the one that came too late, and so that the
    /// effect has been carried out twice.
    /// </param>
    public static Attempt LeaseLost(Exception? failure) => new(AttemptStatus.LeaseLost, failure);
}
