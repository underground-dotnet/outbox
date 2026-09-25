namespace Underground.Outbox.Exceptions;

/// <summary>
/// Raised when an inbox outcome write matched no row although the claim's row lock was still held, which means the
/// claim handed out a row other than the one it locked.
/// </summary>
internal sealed class InboxClaimLostException : InvalidOperationException
{
    internal InboxClaimLostException(long messageId) : base(
        $"The outcome write for inbox message {messageId} matched no row although its row lock was held. The attempt was rolled back and the message will be offered again."
    )
    {
    }
}
