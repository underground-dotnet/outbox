using Microsoft.Extensions.DependencyInjection;

using Underground.Outbox.Data;

namespace Underground.Outbox.Domain.Dispatchers;

public interface IMessageDispatcher
{
    public Task ExecuteAsync(IServiceScope scope, IMessage message, CancellationToken cancellationToken = default);
}
