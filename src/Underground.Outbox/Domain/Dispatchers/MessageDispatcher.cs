using Microsoft.Extensions.DependencyInjection;

using Underground.Outbox.Data;
using Underground.Outbox.Exceptions;

namespace Underground.Outbox.Domain.Dispatchers;

/// <summary>
/// Looks a claimed message up in the <see cref="HandlerRegistry{TEntity}"/> and runs the entry it finds.
/// </summary>
internal sealed class MessageDispatcher<TEntity>(
    HandlerRegistry<TEntity> registry
) : IMessageDispatcher<TEntity> where TEntity : class, IMessage
{
    public Task ExecuteAsync(IServiceScope scope, TEntity message, CancellationToken cancellationToken)
    {
        if (!registry.TryGetEntry(message.Type, out var entry))
        {
            throw new ParsingException($"No handler configured for message type {message.Type} of message: {message.Id}");
        }

        var metadata = new MessageMetadata(message.EventId, message.GroupKey, message.RetryCount);

        return entry.HandleAsync(scope.ServiceProvider, message, metadata, cancellationToken);
    }
}
