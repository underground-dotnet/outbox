using System.Reflection;

namespace Underground.Outbox.Exceptions;

public class MessageHandlerException(HandlerType handlerType, MethodInfo methodInfo, string message, Exception innerException) : Exception(message)
{
    public HandlerType HandlerType { get; } = handlerType;
    public MethodInfo MethodInfo { get; } = methodInfo;

    public new Exception InnerException => innerException;
}
