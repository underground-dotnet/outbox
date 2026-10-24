namespace Underground.Outbox.Exceptions;

/// <summary>
/// Thrown when an inbox message is Staged whose type no inbox Handler handles. Each stored message is addressed
/// to one Handler, so without one there would be no row to claim it.
/// </summary>
public class NoInboxHandlerException : Exception
{
    /// <summary>The stored message type nothing handles.</summary>
    public string MessageTypeName { get; }

    /// <summary>Creates the exception.</summary>
    /// <param name="messageTypeName">The stored message type nothing handles.</param>
    public NoInboxHandlerException(string messageTypeName)
        : base($"No inbox handler handles message type '{messageTypeName}', so the message cannot be addressed to one.")
    {
        MessageTypeName = messageTypeName;
    }
}
