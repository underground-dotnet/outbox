using Underground.Outbox.Data;
using Underground.Outbox.Domain.Dispatchers;
using Underground.Outbox.Exceptions;

namespace Underground.Outbox.Domain.Middleware;

/// <summary>
/// The end of the pipeline: looks the message up in the <see cref="HandlerRegistry{TEntity}"/>, hands it to its
/// Handler and persists whatever the Handler wrote.
/// </summary>
/// <remarks>
/// Scoped and resolved from the scope its Claim is processed in, so the injected provider is that scope's and
/// a Scoped Handler is one instance per message.
/// </remarks>
internal sealed class DispatchMessage<TEntity>(
    HandlerRegistry<TEntity> registry,
    IServiceProvider serviceProvider,
    MessageDbContext<TEntity> messageDbContext
) where TEntity : class, IMessage
{
    private readonly IDbContext _dbContext = messageDbContext.Context;

    /// <summary>
    /// Invokes the Handler for this message. It returns only if the Handler did; the middleware wrapped around
    /// this call decide what a throw means. The save runs in the inbox's transaction, or - on the outbox,
    /// which holds none - in one of EF Core's own.
    /// </summary>
    /// <exception cref="ParsingException">No Handler is registered for the message's type or, on the inbox, its Recipient.</exception>
    internal async Task ExecuteAsync(TEntity message, CancellationToken cancellationToken)
    {
        if (!registry.TryGetEntry(message, out var entry))
        {
            throw new ParsingException(message is IAddressedMessage addressed
                ? $"No handler {addressed.Recipient} configured for message type {message.Type} of message: {message.Id}"
                : $"No handler configured for message type {message.Type} of message: {message.Id}");
        }

        var metadata = new MessageMetadata(message.EventId, message.GroupKey, message.RetryCount);

        await entry.HandleAsync(serviceProvider, message, metadata, cancellationToken).ConfigureAwait(false);

        // in case the handler forgot to call SaveChanges
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
