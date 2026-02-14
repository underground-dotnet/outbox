using System.Data;

using Underground.Outbox;
using Underground.Outbox.Data;

namespace Underground.OutboxTest.TestHandler;

public class FailedMessageHandler : IOutboxMessageHandler<FailedMessage>
{
    public static List<FailedMessage> CalledWith { get; set; } = [];

    public Task HandleAsync(FailedMessage message, MessageMetadata metadata, CancellationToken cancellationToken)
    {
        CalledWith.Add(message);
        throw new DataException("Failed to handle message");
    }
}
