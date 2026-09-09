
using Microsoft.Extensions.Logging;

using Underground.Outbox;
using Underground.Outbox.Attributes;
using Underground.Outbox.Data;

namespace ConsoleApp;

#pragma warning disable CA1848 // Use the LoggerMessage delegates
[InboxHandler<AppDbContext>]
public class InboxMessageHandler(ILogger<InboxMessageHandler> logger) : IInboxMessageHandler<ExampleMessage>
{
    public Task HandleAsync(ExampleMessage message, MessageMetadata metadata, CancellationToken cancellationToken)
    {
        logger.LogInformation("received inbox: messageId: {EventId}, group: {GroupKey}", metadata.EventId, metadata.GroupKey);
        return Task.CompletedTask;
    }
}
#pragma warning restore CA1848 // Use the LoggerMessage delegates
