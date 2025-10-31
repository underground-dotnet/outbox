using System.Data;

using Underground.Outbox;
using Underground.Outbox.Attributes;

namespace Underground.OutboxTest.TestHandler;

public class DiscardFailedMessageHandler : IOutboxMessageHandler<ExampleMessage>
{
    public static List<ExampleMessage> CalledWith { get; set; } = [];

    [DiscardOn(typeof(DataException))]
    public Task HandleAsync(ExampleMessage message, CancellationToken cancellationToken)
    {
        CalledWith.Add(message);
        throw new DataException("Failed to handle message");
    }
}
