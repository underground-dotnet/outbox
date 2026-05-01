namespace Underground.Outbox.Exceptions;

public class MessageHandlerException : Exception
{
    public HandlerType HandlerType { get; }
    public MessageType MessageType { get; }

    public MessageHandlerException(HandlerType handlerType, MessageType messageType, string message, Exception innerException)
        : base(message, innerException)
    {
        HandlerType = handlerType;
        MessageType = messageType;
    }
}
