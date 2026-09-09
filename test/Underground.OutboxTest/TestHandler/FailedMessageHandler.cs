using System.Collections.Concurrent;
using System.Data;

using Underground.Outbox;
using Underground.Outbox.Attributes;
using Underground.Outbox.Data;

namespace Underground.OutboxTest.TestHandler;

[OutboxHandler<TestDbContext>]
public class FailedMessageHandler : IOutboxMessageHandler<FailedMessage>
{
    public static ConcurrentQueue<FailedMessage> CalledWith { get; } = new();

    public Task HandleAsync(FailedMessage message, MessageMetadata metadata, CancellationToken cancellationToken)
    {
        CalledWith.Enqueue(message);
        throw new DataException("Failed to handle message");
    }
}
