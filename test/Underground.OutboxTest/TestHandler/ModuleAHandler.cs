using System.Collections.Concurrent;

using Underground.Outbox;
using Underground.Outbox.Attributes;
using Underground.Outbox.Data;

namespace Underground.OutboxTest.TestHandler;

/// <summary>Handles <see cref="SharedContract"/> for module A.</summary>
[OutboxHandler<ModuleADbContext>]
public class ModuleAHandler : IOutboxMessageHandler<SharedContract>
{
    /// <summary>The messages this module's Handler was given.</summary>
    public static ConcurrentQueue<int> CalledWith { get; } = new();

    /// <inheritdoc />
    public Task HandleAsync(SharedContract message, MessageMetadata metadata, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        CalledWith.Enqueue(message.Id);
        return Task.CompletedTask;
    }
}
