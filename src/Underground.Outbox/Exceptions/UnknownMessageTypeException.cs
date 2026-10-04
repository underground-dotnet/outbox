namespace Underground.Outbox.Exceptions;

/// <summary>
/// Raised when no Handler in this deployment claims the message's stored type.
/// </summary>
public sealed class UnknownMessageTypeException : DeploymentGapException
{
    /// <summary>The stored type no Handler claims.</summary>
    public string MessageTypeName { get; }

    internal UnknownMessageTypeException(string messageTypeName, long messageId)
        : base(messageId, $"No handler in this deployment claims message type '{messageTypeName}' of message {messageId}.")
    {
        MessageTypeName = messageTypeName;
    }
}
