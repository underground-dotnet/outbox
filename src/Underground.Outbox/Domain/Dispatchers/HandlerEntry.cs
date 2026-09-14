using Underground.Outbox.Data;

namespace Underground.Outbox.Domain.Dispatchers;

/// <summary>
/// One handler's claim on one message type, contributed by the assembly that declares the handler.
/// </summary>
/// <remarks>
/// <see cref="MessageTypeName"/> is evaluated from <c>typeof(T).FullName</c> at run time rather than
/// written as a literal, because that is the very expression the write side stores in the <c>type</c>
/// column. A literal taken from the compiler's spelling of the type would disagree with it for nested
/// types (<c>Outer.Inner</c> against <c>Outer+Inner</c>) and for generic ones.
/// </remarks>
/// <typeparam name="TEntity">The message table this entry belongs to.</typeparam>
public sealed class HandlerEntry<TEntity> where TEntity : class, IMessage
{
    private readonly Func<IServiceProvider, TEntity, MessageMetadata, CancellationToken, Task> _handle;

    /// <summary>Creates an entry. Called from generated code.</summary>
    /// <param name="messageTypeName">The stored <see cref="IMessage.Type"/> this entry answers to.</param>
    /// <param name="handlerType">The handler that runs it.</param>
    /// <param name="messageType">The payload type it deserializes to.</param>
    /// <param name="handle">Deserializes the payload, resolves the handler and invokes it.</param>
    public HandlerEntry(
        string messageTypeName,
        HandlerType handlerType,
        MessageType messageType,
        Func<IServiceProvider, TEntity, MessageMetadata, CancellationToken, Task> handle)
    {
        MessageTypeName = messageTypeName;
        HandlerType = handlerType;
        MessageType = messageType;
        _handle = handle;
    }

    /// <summary>The stored <see cref="IMessage.Type"/> this entry answers to.</summary>
    public string MessageTypeName { get; }

    /// <summary>The handler that runs the message.</summary>
    public HandlerType HandlerType { get; }

    /// <summary>The payload type the message deserializes to.</summary>
    public MessageType MessageType { get; }

    /// <summary>Runs the message through its handler.</summary>
    /// <param name="serviceProvider">The scope's provider, from which the handler is resolved.</param>
    /// <param name="message">The claimed message.</param>
    /// <param name="metadata">Metadata handed to the handler.</param>
    /// <param name="cancellationToken">Cancelled when the handler's time runs out.</param>
    public Task HandleAsync(
        IServiceProvider serviceProvider,
        TEntity message,
        MessageMetadata metadata,
        CancellationToken cancellationToken)
        => _handle(serviceProvider, message, metadata, cancellationToken);
}
