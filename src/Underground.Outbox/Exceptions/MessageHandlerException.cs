namespace Underground.Outbox.Exceptions;

public class MessageHandlerException(string message, Exception innerException) : Exception(message)
{
    public new Exception InnerException => innerException;
}
