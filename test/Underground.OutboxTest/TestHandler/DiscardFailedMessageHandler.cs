using System.Data;

using Underground.Outbox;
using Underground.Outbox.Attributes;

namespace Underground.OutboxTest.TestHandler;

public class DiscardFailedMessageHandler : IOutboxMessageHandler<ExampleMessage>
{
    [DiscardOn(typeof(DataException))]
    public Task HandleAsync(ExampleMessage message, CancellationToken cancellationToken)
    {
        throw new DataException("Failed to handle message");
    }
}
