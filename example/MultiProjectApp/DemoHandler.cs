using MultiProjectLib;

using Underground.Outbox;
using Underground.Outbox.Attributes;
using Underground.Outbox.Data;

namespace MultiProjectApp;

[OutboxHandler<AppDbContext>]
public class DemoHandler : IOutboxMessageHandler<DemoMessage>
{
    public Task HandleAsync(DemoMessage message, MessageMetadata metadata, CancellationToken cancellationToken)
    {
#pragma warning disable MA0025 // Implement the functionality instead of throwing NotImplementedException
        throw new NotImplementedException();
#pragma warning restore MA0025 // Implement the functionality instead of throwing NotImplementedException
    }
}
