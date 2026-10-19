namespace Underground.Outbox.Exceptions;

/// <summary>
/// Thrown when an inbox message is staged whose type no Inbox has a Handler for, so it would be applied
/// nowhere.
/// </summary>
internal sealed class NoInboxHandlesMessageException : InvalidOperationException
{
    internal NoInboxHandlesMessageException(string messageTypeName) : base(
        $"No Inbox has a handler for message type '{messageTypeName}', so staging it would write nothing."
    )
    {
    }
}
