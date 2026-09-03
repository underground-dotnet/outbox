
using Microsoft.Extensions.Logging;

using Underground.Outbox;
using Underground.Outbox.Data;

namespace ConsoleApp;

#pragma warning disable CA1848 // Use the LoggerMessage delegates
public class ExampleMessageHandler(ILogger<ExampleMessageHandler> logger) : IOutboxMessageHandler<ExampleMessage>, IOutboxMessageHandler<SecondMessage>
{
    public Task HandleAsync(ExampleMessage message, MessageMetadata metadata, CancellationToken cancellationToken)
    {

        logger.LogInformation("received outbox: messageId: {EventId}, group: {GroupKey}", metadata.EventId, metadata.GroupKey);
        return Task.CompletedTask;
    }

    public Task HandleAsync(SecondMessage message, MessageMetadata metadata, CancellationToken cancellationToken)
    {
        logger.LogInformation("received second message type: messageId: {EventId}, group: {GroupKey}", metadata.EventId, metadata.GroupKey);
        return Task.CompletedTask;
    }
}
#pragma warning restore CA1848 // Use the LoggerMessage delegates
