using System.Collections.Concurrent;
using System.Data;

using Underground.Outbox;
using Underground.Outbox.Data;

namespace Underground.OutboxTest.TestHandler;

public class SharedMessageAuditHandler : IInboxMessageHandler<SharedMessage>
{
    public static ConcurrentQueue<int> CalledWith { get; } = new();

    public Task HandleAsync(SharedMessage message, MessageMetadata metadata, CancellationToken cancellationToken)
    {
        CalledWith.Enqueue(message.Id);

        if (message.AuditFails)
        {
            throw new DataException("Failed to audit message");
        }

        return Task.CompletedTask;
    }
}
