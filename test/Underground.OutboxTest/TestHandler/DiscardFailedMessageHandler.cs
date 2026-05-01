using System.Data;

using Underground.Outbox;
using Underground.Outbox.Data;

namespace Underground.OutboxTest.TestHandler;

public class DiscardFailedMessageHandler : IOutboxMessageHandler<DiscardMessage>
{
    public Task HandleAsync(DiscardMessage message, MessageMetadata metadata, CancellationToken cancellationToken)
    {
        throw new DataException("Failed to handle message");
    }
}
