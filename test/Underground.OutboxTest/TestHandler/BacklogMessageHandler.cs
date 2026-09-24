using System.Collections.Concurrent;

using Underground.Outbox;
using Underground.Outbox.Data;

namespace Underground.OutboxTest.TestHandler;

/// <summary>Records the ids it ran, on either side.</summary>
public class BacklogMessageHandler : IOutboxMessageHandler<BacklogMessage>, IInboxMessageHandler<BacklogMessage>
{
    public static ConcurrentQueue<int> CalledWith { get; } = new();

    public Task HandleAsync(BacklogMessage message, MessageMetadata metadata, CancellationToken cancellationToken)
    {
        CalledWith.Enqueue(message.Id);
        return Task.CompletedTask;
    }
}
