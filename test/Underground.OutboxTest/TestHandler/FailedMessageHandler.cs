using System.Data;

using Underground.Outbox;

namespace Underground.OutboxTest.TestHandler;

public class FailedMessageHandler : IOutboxMessageHandler<ExampleMessage>
{
    public static List<ExampleMessage> CalledWith { get; set; } = [];

    public Task HandleAsync(ExampleMessage message, CancellationToken cancellationToken)
    {
        CalledWith.Add(message);
        throw new DataException("Failed to handle message");
    }
}
