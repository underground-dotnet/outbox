namespace Underground.Outbox.Exceptions;

internal class NoActiveTransactionException : InvalidOperationException
{
    internal NoActiveTransactionException(string tableName) : base(
        $"Adding messages to the {tableName} requires an active database transaction. Stage them instead to have the caller's own save write them."
    )
    {
    }
}
