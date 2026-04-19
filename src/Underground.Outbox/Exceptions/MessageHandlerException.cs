namespace Underground.Outbox.Exceptions;

public class MessageHandlerException : Exception
{
    public HandlerType HandlerType { get; }

    public MessageHandlerException(HandlerType handlerType, string message, Exception innerException)
        : base(message, innerException)
    {
        HandlerType = handlerType;
    }
}
