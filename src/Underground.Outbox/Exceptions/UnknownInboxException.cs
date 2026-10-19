namespace Underground.Outbox.Exceptions;

/// <summary>
/// Thrown as the host starts when a Handler is bound to an Inbox no <c>AddInboxServices</c> call registered.
/// </summary>
internal sealed class UnknownInboxException : InvalidOperationException
{
    internal UnknownInboxException(string? inbox, HandlerType handlerType) : base(
        $"Handler {handlerType.FullName} is bound to the Inbox '{inbox}', which is not registered. "
        + "Register it with AddInboxServices, or bind the handler's assembly to an Inbox that is."
    )
    {
    }
}
