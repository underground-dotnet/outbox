using Microsoft.Extensions.DependencyInjection;

using Underground.Outbox.Data;

namespace Underground.Outbox.Domain.Dispatcher;

public interface IMessageDispatcher
{
    public Task<ProcessingResult> ExecuteAsync(IServiceScope scope, OutboxMessage message, CancellationToken cancellationToken);
}
