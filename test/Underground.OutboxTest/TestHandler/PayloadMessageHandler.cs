using Underground.Outbox;
using Underground.Outbox.Data;

namespace Underground.OutboxTest.TestHandler;

/// <summary>
/// Always succeeds, on both sides. Tests break its messages by writing a payload it cannot read.
/// </summary>
public class PayloadMessageHandler : IOutboxMessageHandler<PayloadMessage>, IInboxMessageHandler<PayloadMessage>
{
    public Task HandleAsync(PayloadMessage message, MessageMetadata metadata, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
