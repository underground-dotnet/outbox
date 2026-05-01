namespace Underground.Outbox.SourceGenerator;

#pragma warning disable MA0048 // File name must match type name
internal enum HandlerKind
#pragma warning restore MA0048 // File name must match type name
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
