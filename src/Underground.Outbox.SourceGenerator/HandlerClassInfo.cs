namespace Underground.Outbox.SourceGenerator;

internal enum HandlerKind
{
    Outbox,
    Inbox
}

internal readonly record struct HandlerClassInfo
{
    internal string HandlerFullName { get; }
    internal string MessageTypeFullName { get; }
    internal HandlerKind Kind { get; }
    internal EquatableList<string> DiscardOnExceptionTypeFullNames { get; }

    public HandlerClassInfo(
        string handlerFullName,
        string messageTypeFullName,
        HandlerKind kind,
        EquatableList<string>? discardOnExceptionTypeFullNames = null)
    {
        HandlerFullName = handlerFullName;
        MessageTypeFullName = messageTypeFullName;
        Kind = kind;
        DiscardOnExceptionTypeFullNames = discardOnExceptionTypeFullNames ?? [];
    }
}
