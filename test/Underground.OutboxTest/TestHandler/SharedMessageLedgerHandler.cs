using System.Collections.Concurrent;

using Underground.Outbox;
using Underground.Outbox.Data;

namespace Underground.OutboxTest.TestHandler;

public class SharedMessageLedgerHandler : IInboxMessageHandler<SharedMessage>
{
    public static ConcurrentQueue<int> CalledWith { get; } = new();

    public Task HandleAsync(SharedMessage message, MessageMetadata metadata, CancellationToken cancellationToken)
    {
        CalledWith.Enqueue(message.Id);
        return Task.CompletedTask;
    }
}
