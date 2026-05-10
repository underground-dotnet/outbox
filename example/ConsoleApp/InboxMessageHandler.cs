
using Microsoft.Extensions.Logging;

using Underground.Outbox;
using Underground.Outbox.Data;

namespace ConsoleApp;

#pragma warning disable CA1848 // Use the LoggerMessage delegates
public class InboxMessageHandler(ILogger<InboxMessageHandler> logger) : IInboxMessageHandler<ExampleMessage>
{
    public Task HandleAsync(ExampleMessage message, MessageMetadata metadata, CancellationToken cancellationToken)
    {
        logger.LogInformation("received inbox: messageId: {EventId}, partition: {PartitionKey}", metadata.EventId, metadata.PartitionKey);
        return Task.CompletedTask;
    }
}
#pragma warning restore CA1848 // Use the LoggerMessage delegates
