
using Underground.Outbox;
using Underground.Outbox.Data;

namespace ConsoleApp;

public class ExampleMessageHandler : IOutboxMessageHandler<ExampleMessage>
{
    public Task HandleAsync(ExampleMessage message, MessageMetadata metadata, CancellationToken cancellationToken)
    {
        Console.WriteLine($"received outbox: messageId: {metadata.EventId}, partition: {metadata.PartitionKey}");
        return Task.CompletedTask;
    }
}
