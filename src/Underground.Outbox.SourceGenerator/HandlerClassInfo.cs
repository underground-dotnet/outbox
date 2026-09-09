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

    /// <summary>
    /// The message type as the generated code writes it: fully qualified, <c>global::</c> included, so no
    /// consumer namespace can shadow it. Not the runtime <see cref="System.Type.FullName"/> the message
    /// carries in its <c>type</c> column - for nested and generic types the two spellings differ.
    /// </summary>
    internal string MessageTypeQualifiedName { get; }

    /// <summary>The message type as a diagnostic names it, e.g. <c>Sample.Outer.Inner</c>.</summary>
    internal string MessageTypeDisplayName { get; }

    /// <summary>
    /// The <c>DbContext</c> named in the handler's <c>[OutboxHandler&lt;T&gt;]</c> or
    /// <c>[InboxHandler&lt;T&gt;]</c> attribute, fully qualified. Empty when the handler carries no
    /// attribute, which is <c>OUTBOX002</c>.
    /// </summary>
    internal string ContextQualifiedName { get; }

    /// <summary>That same context as prose names it, e.g. <c>Sample.OrdersContext</c>.</summary>
    internal string ContextDisplayName { get; }

    internal HandlerKind Kind { get; }

    public HandlerClassInfo(
        string handlerFullName,
        string messageTypeQualifiedName,
        string messageTypeDisplayName,
        string contextQualifiedName,
        string contextDisplayName,
        HandlerKind kind)
    {
        HandlerFullName = handlerFullName;
        MessageTypeQualifiedName = messageTypeQualifiedName;
        MessageTypeDisplayName = messageTypeDisplayName;
        ContextQualifiedName = contextQualifiedName;
        ContextDisplayName = contextDisplayName;
        Kind = kind;
    }
}
