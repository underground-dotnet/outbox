namespace Underground.Outbox.Exceptions;

public class NoActiveTransactionException : InvalidOperationException
{
    public NoActiveTransactionException() : base("Adding messages to the Outbox requires an active database transaction.")
    {
    }
}