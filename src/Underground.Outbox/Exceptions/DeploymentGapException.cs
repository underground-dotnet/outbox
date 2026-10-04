namespace Underground.Outbox.Exceptions;

/// <summary>
/// A failed Processing Attempt caused by what the running deployment lacks rather than by the message, so the
/// next deployment may heal it.
/// </summary>
/// <remarks>
/// It matches only an Exception Policy whose exception type is this one or a subclass, never a broader one
/// such as <see cref="Exception"/>, so a catch-all policy cannot discard messages a later deployment could
/// have handled. See ADR 0012.
/// </remarks>
public abstract class DeploymentGapException : Exception
{
    /// <summary>The message that could not be handled.</summary>
    public long MessageId { get; }

    private protected DeploymentGapException(long messageId, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        MessageId = messageId;
    }
}
