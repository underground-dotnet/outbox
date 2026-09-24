using System.Collections.Concurrent;

using Underground.Outbox;
using Underground.Outbox.Data;

namespace Underground.OutboxTest.TestHandler;

/// <summary>Records which side ran it, and nothing else.</summary>
public class SeparateContextMessageHandler : IOutboxMessageHandler<SeparateContextMessage>, IInboxMessageHandler<SeparateContextMessage>
{
    public static ConcurrentQueue<int> CalledWith { get; } = new();

    public Task HandleAsync(SeparateContextMessage message, MessageMetadata metadata, CancellationToken cancellationToken)
    {
        CalledWith.Enqueue(message.Id);
        return Task.CompletedTask;
    }
}
