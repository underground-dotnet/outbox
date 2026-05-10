using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Underground.Outbox.Configuration;
using Underground.Outbox.Data;
using Underground.Outbox.Domain;

namespace Underground.OutboxTest;

internal sealed class NoPollingProcessor<TEntity>(
    ILogger<ConcurrentProcessor<TEntity>> logger,
    IServiceScopeFactory scopeFactory,
    ServiceConfiguration<TEntity> config
) : ConcurrentProcessor<TEntity>(logger, scopeFactory, config) where TEntity : class, IMessage
{
    internal override async Task StartAsync(CancellationToken cancellationToken)
    {
        // only setup workers, but skip the polling part
        CreateWorkers(cancellationToken);
    }
}
