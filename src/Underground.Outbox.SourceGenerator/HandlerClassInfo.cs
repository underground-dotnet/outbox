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
    /// The message type written as C# source, e.g. <c>Sample.Outer.Inner</c>. This is a type reference for
    /// the generated code, not the runtime <see cref="System.Type.FullName"/> the message carries in its
    /// <c>type</c> column - for nested and generic types the two spellings differ.
    /// </summary>
    internal string MessageTypeDisplayName { get; }

    internal HandlerKind Kind { get; }

    /// <summary>
    /// The <c>ServiceLifetime</c> member named by <c>[MessageHandlerLifetime]</c>, or
    /// <c>"Transient"</c> when the handler does not carry the attribute.
    /// </summary>
    internal string Lifetime { get; }

    public HandlerClassInfo(string handlerFullName, string messageTypeDisplayName, HandlerKind kind, string lifetime)
    {
        HandlerFullName = handlerFullName;
        MessageTypeDisplayName = messageTypeDisplayName;
        Kind = kind;
        Lifetime = lifetime;
    }
}
