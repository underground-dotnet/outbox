namespace Underground.Outbox.SourceGenerator;

internal readonly record struct HandlerClassInfo
{
    internal readonly string FullNamespace;
    internal readonly string ClassName { get; }
    internal readonly string MessageType;

    public HandlerClassInfo(string fullNamespace, string className, string messageType)
    {
        FullNamespace = fullNamespace;
        ClassName = className;
        MessageType = messageType;
    }
}
