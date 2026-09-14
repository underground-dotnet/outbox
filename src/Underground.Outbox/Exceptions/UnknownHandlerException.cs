namespace Underground.Outbox.Exceptions;

/// <summary>
/// Thrown when configuration names a handler and message type that discovery did not find, so the
/// configuration would silently have no effect.
/// </summary>
/// <remarks>
/// Usually the handler's project does not run the source generator, or the pair names a message type
/// the handler does not actually handle.
/// </remarks>
public class UnknownHandlerException : Exception
{
    /// <summary>The handler the configuration named.</summary>
    public HandlerType HandlerType { get; }

    /// <summary>The message type the configuration named.</summary>
    public MessageType MessageType { get; }

    /// <summary>Creates the exception.</summary>
    /// <param name="handlerType">The handler the configuration named.</param>
    /// <param name="messageType">The message type the configuration named.</param>
    public UnknownHandlerException(HandlerType handlerType, MessageType messageType)
        : base($"Configuration names handler '{handlerType.FullName}' for message type '{messageType.FullName}', but no such handler was discovered. Check that the handler's project references Underground.Outbox.SourceGenerator and that its generated registration method is called.")
    {
        HandlerType = handlerType;
        MessageType = messageType;
    }
}
