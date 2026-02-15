using Underground.Outbox;
using Underground.Outbox.Data;

namespace MultiProjectApp;

public class DemoHandler : IOutboxMessageHandler<DemoMessage>
{
    public Task HandleAsync(DemoMessage message, MessageMetadata metadata, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}
