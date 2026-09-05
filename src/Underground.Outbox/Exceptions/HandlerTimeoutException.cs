namespace Underground.Outbox.Exceptions;

/// <summary>
/// Raised when a Handler was still running once the time it was given ran out.
/// </summary>
/// <remarks>
/// Deliberately not an <see cref="OperationCanceledException"/>, even though a cancellation is what
/// produced it. The stages between a Handler and its worker step aside for a cancellation, because a
/// cancellation means the application is shutting down and the transaction is about to be discarded
/// whole. A Handler that ran out of time is the opposite case: the transaction lives on, so the
/// Handler's writes have to be rolled back and the attempt recorded like any other failure.
/// </remarks>
public class HandlerTimeoutException : TimeoutException
{
    /// <summary>The message whose Handler ran out of time.</summary>
    public long MessageId { get; }

    /// <summary>The time that Handler was given.</summary>
    public TimeSpan Timeout { get; }

    internal HandlerTimeoutException(long messageId, TimeSpan timeout, Exception innerException)
        : base($"The handler for message {messageId} was cancelled after {timeout}.", innerException)
    {
        MessageId = messageId;
        Timeout = timeout;
    }
}
