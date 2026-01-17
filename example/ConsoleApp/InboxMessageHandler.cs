
using Underground.Outbox;
using Underground.Outbox.Data;

namespace ConsoleApp;

public class InboxMessageHandler : IInboxMessageHandler<ExampleMessage>
{
    public Task HandleAsync(ExampleMessage message, MessageMetadata metadata, CancellationToken cancellationToken)
    {
        Console.WriteLine($"received inbox: messageId: {metadata.EventId}, partition: {metadata.PartitionKey}");
        return Task.CompletedTask;
    }
}
