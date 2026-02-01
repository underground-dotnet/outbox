using MultiProjectLib;

using Underground.Outbox;
using Underground.Outbox.Data;

namespace MultiProjectApp;

public class DemoHandler : IOutboxMessageHandler<ExampleMessage>
{
    public Task HandleAsync(ExampleMessage message, MessageMetadata metadata, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}
