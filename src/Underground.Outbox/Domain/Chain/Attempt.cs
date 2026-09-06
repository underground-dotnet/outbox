namespace Underground.Outbox.Domain.Chain;

/// <summary>
/// What became of one run of the Chain against one Head, passed back out through the Stages. A summary of
/// what has already been recorded, not the means of recording it.
/// </summary>
/// <remarks>
/// The constructor is private because two of the four field combinations are nonsense; the factories
/// below are the only shapes that exist.
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
    /// What the Handler threw, if it threw. Null on <see cref="AttemptStatus.Handled"/>, and on a
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
    /// What the Handler threw, if it threw. No failure means the Handler succeeded and the completion write
    /// came too late, so the effect has been carried out twice.
    /// </param>
    public static Attempt LeaseLost(Exception? failure) => new(AttemptStatus.LeaseLost, failure);
}
