using System.Collections.Concurrent;

using Underground.Outbox;
using Underground.Outbox.Attributes;
using Underground.Outbox.Data;

namespace Underground.OutboxTest.TestHandler;

[OutboxHandler<TestDbContext>]
public class SecondMessageHandler : IOutboxMessageHandler<SecondMessage>
{
    public static ConcurrentQueue<SecondMessage> CalledWith { get; } = new();

    public Task HandleAsync(SecondMessage message, MessageMetadata metadata, CancellationToken cancellationToken)
    {
        CalledWith.Enqueue(message);
        return Task.CompletedTask;
    }
}
