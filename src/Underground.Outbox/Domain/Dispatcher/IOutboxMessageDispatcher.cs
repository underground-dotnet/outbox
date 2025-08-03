using Underground.Outbox.Data;

namespace Underground.Outbox.Domain.Dispatcher;

public interface IOutboxMessageDispatcher
{
    public Task<ProcessingResult> ExecuteAsync(OutboxMessage message, CancellationToken cancellationToken);
}
