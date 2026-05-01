namespace Underground.Outbox.Exceptions;

internal class NoActiveTransactionException : InvalidOperationException
{
    internal NoActiveTransactionException() : base("Adding messages to the Outbox requires an active database transaction.")
    {
    }
}