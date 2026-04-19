using Underground.Outbox;
using Underground.Outbox.Data;

namespace MultiProjectLib;

public class ExampleMessageHandler : IOutboxMessageHandler<ExampleMessage>
{
    public Task HandleAsync(ExampleMessage message, MessageMetadata metadata, CancellationToken cancellationToken)
    {
        Console.WriteLine("received outbox: " + message.Id);
        return Task.CompletedTask;
    }
}
