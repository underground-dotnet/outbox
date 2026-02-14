using System.Reflection;

namespace Underground.Outbox.Exceptions;

public class MessageHandlerException(HandlerType handlerType, string message, Exception innerException) : Exception(message)
{
    public HandlerType HandlerType { get; } = handlerType;

    public new Exception InnerException => innerException;
}
