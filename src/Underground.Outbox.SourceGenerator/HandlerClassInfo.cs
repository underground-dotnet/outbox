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

    public HandlerClassInfo(string handlerFullName, string messageTypeFullName, HandlerKind kind)
    {
        HandlerFullName = handlerFullName;
        MessageTypeFullName = messageTypeFullName;
        Kind = kind;
    }
}
