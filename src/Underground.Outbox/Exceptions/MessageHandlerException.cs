namespace Underground.Outbox.Exceptions;

public class MessageHandlerException(string message, Exception innerException, IOutboxErrorHandler errorHandler) : Exception(message)
{
    public IOutboxErrorHandler ErrorHandler { get; } = errorHandler;
    public new Exception InnerException => innerException;
}
