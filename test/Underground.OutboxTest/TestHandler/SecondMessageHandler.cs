using Underground.Outbox;
using Underground.Outbox.Data;

namespace Underground.OutboxTest.TestHandler;

public class SecondMessageHandler : IOutboxMessageHandler<SecondMessage>
{
    public static List<SecondMessage> CalledWith { get; set; } = [];

    public Task HandleAsync(SecondMessage message, MessageMetadata metadata, CancellationToken cancellationToken)
    {
        CalledWith.Add(message);
        return Task.CompletedTask;
    }
}
