using Microsoft.Extensions.DependencyInjection;

using Underground.Outbox.Data;

namespace Underground.Outbox.Domain.Dispatchers;

public interface IMessageDispatcher<in TEntity> where TEntity : class, IMessage
{
    public Task ExecuteAsync(IServiceScope scope, TEntity message, CancellationToken cancellationToken = default);
}
