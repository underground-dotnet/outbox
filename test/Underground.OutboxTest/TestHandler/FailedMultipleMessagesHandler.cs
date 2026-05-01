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

#pragma warning disable MA0048 // File name must match type name
public record FailedMultiMessageA(int Id);
public record FailedMultiMessageB(int Id);
#pragma warning restore MA0048 // File name must match type name
