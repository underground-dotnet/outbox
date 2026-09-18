namespace Underground.Outbox.Exceptions;

/// <summary>
/// Thrown when two handlers claim the same message type, so which one would run is not something the
/// author chose.
/// </summary>
/// <remarks>
/// Within one assembly the source generator reports this at compile time as OUTBOX001. Across
/// assemblies no single compilation sees both handlers, so this throw is the earliest it can be
/// caught; it happens as the host starts, before any message is claimed.
/// </remarks>
public class CompetingHandlersException : Exception
{
    /// <summary>The stored message type both handlers claim.</summary>
    public string MessageTypeName { get; }

    /// <summary>The handler already holding the claim.</summary>
    public HandlerType ExistingHandlerType { get; }

    /// <summary>The handler that tried to take it.</summary>
    public HandlerType CompetingHandlerType { get; }

    /// <summary>Creates the exception.</summary>
    /// <param name="messageTypeName">The stored message type both handlers claim.</param>
    /// <param name="existingHandlerType">The handler already holding the claim.</param>
    /// <param name="competingHandlerType">The handler that tried to take it.</param>
    public CompetingHandlersException(
        string messageTypeName,
        HandlerType existingHandlerType,
        HandlerType competingHandlerType)
        : base($"Message type '{messageTypeName}' is claimed by more than one handler ({existingHandlerType.FullName}, {competingHandlerType.FullName}). Only one handler may run a message type.")
    {
        MessageTypeName = messageTypeName;
        ExistingHandlerType = existingHandlerType;
        CompetingHandlerType = competingHandlerType;
    }
}
