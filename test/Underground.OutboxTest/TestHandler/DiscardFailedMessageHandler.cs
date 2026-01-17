using System.Data;

using Underground.Outbox;
using Underground.Outbox.Attributes;
using Underground.Outbox.Data;

namespace Underground.OutboxTest.TestHandler;

public class DiscardFailedMessageHandler : IOutboxMessageHandler<ExampleMessage>
{
    [DiscardOn(typeof(DataException))]
    public Task HandleAsync(ExampleMessage message, MessageMetadata metadata, CancellationToken cancellationToken)
    {
        throw new DataException("Failed to handle message");
    }
}
