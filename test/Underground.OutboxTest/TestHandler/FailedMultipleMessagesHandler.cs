using Underground.Outbox;
using Underground.Outbox.Data;

namespace Underground.OutboxTest.TestHandler;

public class FailedMultipleMessagesHandler : IOutboxMessageHandler<FailedMultiMessageA>, IOutboxMessageHandler<FailedMultiMessageB>
{
    public Task HandleAsync(FailedMultiMessageA message, MessageMetadata metadata, CancellationToken cancellationToken)
    {
        throw new InvalidOperationException("Failed to handle message A");
    }

    public Task HandleAsync(FailedMultiMessageB message, MessageMetadata metadata, CancellationToken cancellationToken)
    {
        throw new InvalidOperationException("Failed to handle message B");
    }
}

public record FailedMultiMessageA(int Id);
public record FailedMultiMessageB(int Id);
