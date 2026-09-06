namespace Underground.Outbox.Exceptions;

/// <summary>
/// Raised when a Handler was still running once the time it was given ran out.
/// </summary>
/// <remarks>
/// Deliberately not an <see cref="OperationCanceledException"/>, which the middleware step aside for because
/// it means the transaction is about to be discarded whole. Here the transaction lives on, so the
/// Handler's writes are rolled back and the attempt recorded like any other failure.
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
